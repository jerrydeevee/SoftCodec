using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SoftCodec.Core;
using SIPSorcery.SIP;
namespace SoftCodec.Sip;
public enum Admission { Allow, Drop, Challenge }
public sealed class SecurityFilter(SecurityOptions options,ISecretStore secrets,TimeProvider? clock=null)
{
 readonly byte[] key=RandomNumberGenerator.GetBytes(32);readonly object gate=new();readonly Dictionary<string,(long Window,int Count)> rates=new();readonly Dictionary<string,(int Count,long At)> replay=new();
 readonly Ipv4Cidr[] allowlist=options.AllowlistCidrs.Select(Ipv4Cidr.Parse).ToArray();readonly TimeProvider time=clock??TimeProvider.System;
 public Admission Check(SIPRequest request,IPAddress source)
 {
  if(request.Body?.Length>32768)return Admission.Drop;
  if(allowlist.Length>0&&!allowlist.Any(c=>c.Contains(source)))return Admission.Drop;
  long now=time.GetUtcNow().ToUnixTimeSeconds();
  lock(gate){string ip=source.ToString();if(rates.Count>=4096&&!rates.ContainsKey(ip)){foreach(var k in rates.Where(x=>now-x.Value.Window>=60).Select(x=>x.Key).ToArray())rates.Remove(k);if(rates.Count>=4096)return Admission.Drop;}var bucket=rates.GetValueOrDefault(ip);if(now-bucket.Window>=60)bucket=(now,0);bucket.Count++;rates[ip]=bucket;if(bucket.Count>options.InviteRateLimitPerMinute)return Admission.Drop;}
  if(options.DropScannerUserAgents && new[]{"friendly-scanner","sipvicious","sipcli"}.Any(s=>(request.Header.UserAgent??"").Contains(s,StringComparison.OrdinalIgnoreCase)))return Admission.Drop;
  if(!options.Digest.Enabled)return allowlist.Length>0?Admission.Allow:Admission.Drop;
  var d=request.Header.AuthenticationHeaders?.FirstOrDefault(x=>x.AuthorisationType==SIPAuthorisationHeadersEnum.Authorize)?.SIPDigest;
  string? password=secrets.Read(options.Digest.PasswordSecretRef);
  if(string.IsNullOrEmpty(password)||d is null||d.Username!=options.Digest.Username||d.Realm!=options.Digest.Realm||d.URI!=request.URI.ToString()||!ValidNonce(d.Nonce,source,now)||d.DigestAlgorithm!=DigestAlgorithmsEnum.MD5)return Admission.Challenge;
  if(d.Qop!="auth"||string.IsNullOrEmpty(d.Cnonce)||d.NonceCount<1)return Admission.Challenge;
  string ha1=Md5($"{d.Username}:{d.Realm}:{password}"),ha2=Md5($"{request.Method}:{d.URI}");string expected=Md5($"{ha1}:{d.Nonce}:{d.NonceCount:x8}:{d.Cnonce}:auth:{ha2}");
  if(!Equal(expected,d.Response))return Admission.Challenge;
  lock(gate){string id=d.Nonce+":"+d.Cnonce;if(replay.TryGetValue(id,out var old)&&d.NonceCount<=old.Count)return Admission.Challenge;if(replay.Count>=4096){foreach(var k in replay.Where(x=>now-x.Value.At>300).Select(x=>x.Key).ToArray())replay.Remove(k);if(replay.Count>=4096)return Admission.Drop;}replay[id]=(d.NonceCount,now);}
  return Admission.Allow;
 }
 public SIPAuthenticationHeader Challenge(IPAddress source)
 {
  string nonce=CreateNonce(source,time.GetUtcNow().ToUnixTimeSeconds());
  var d=new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.WWWAuthenticate,DigestAlgorithmsEnum.MD5){Realm=options.Digest.Realm,Nonce=nonce,Qop="auth"};return new(d);
 }
 string CreateNonce(IPAddress ip,long seconds){string text=$"{seconds}:{ip}";return seconds.ToString(CultureInfo.InvariantCulture)+"-"+Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(text)));}
 bool ValidNonce(string? nonce,IPAddress ip,long now){if(nonce is null)return false;var p=nonce.Split('-',2);return p.Length==2&&long.TryParse(p[0],out long seconds)&&seconds<=now&&now-seconds<=300&&Equal(nonce,CreateNonce(ip,seconds));}
 static bool Equal(string? a,string? b)=>a is not null&&b is not null&&CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a.ToLowerInvariant()),Encoding.ASCII.GetBytes(b.ToLowerInvariant()));
 static string Md5(string text)=>Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
