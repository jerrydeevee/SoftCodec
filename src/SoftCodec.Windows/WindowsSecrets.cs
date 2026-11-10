using System.Security.Cryptography;
using System.Text;
using SoftCodec.Core;
namespace SoftCodec.Windows;
public sealed class WindowsSecrets(string directory) : ISecretStore
{
 string PathFor(string reference){if(string.IsNullOrWhiteSpace(reference)||reference.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'&&c!='_'))throw new ArgumentException("Ongeldige secretreferentie.");return Path.Combine(directory,reference+".bin");}
 public string? Read(string reference){string path=PathFor(reference);if(!File.Exists(path))return null;var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);try{return Encoding.UTF8.GetString(plain);}finally{CryptographicOperations.ZeroMemory(plain);}}
 public void Write(string reference,string secret){Directory.CreateDirectory(directory);var plain=Encoding.UTF8.GetBytes(secret);try{File.WriteAllBytes(PathFor(reference),ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser));}finally{CryptographicOperations.ZeroMemory(plain);}}
}
