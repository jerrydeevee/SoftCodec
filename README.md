# SoftCodec — ontwikkel- en testbuild 0.1.0

Windows x64-studiocodec voor Radio Aa en Hunze. Ontwikkeld vanuit `ONTWERP.md`.

**Dit is een testbuild, geen vrijgegeven studioproduct.** De protocolkern is automatisch getest met echte lokale UDP-verbindingen. Deze Linux-bouwomgeving heeft geen Windows-geluidskaart of Comrex; WASAPI, UI-bediening, WAN/NAT en interoperabiliteit zijn nog niet op de doelhardware geverifieerd. Zie `docs/STATUS.md` voor de concrete grenzen.

## Windows-testbuild starten

1. Pak het volledige Windows-pakket uit. Houd alle DLL's naast `SoftCodec.App.exe`.
2. Start `SoftCodec.App.exe` als de normale studio-gebruiker. De self-contained build bevat .NET; een losse runtime is niet nodig.
3. Open **Instellingen**. Kies render naar de mengtafel en capture van de aux/mix-minus. Kanaalnummering begint bij 0.
4. Stel het publieke IPv4-adres in als de studio achter NAT zit. STUN-detectie kan het adres bepalen, maar opent geen routerpoorten.
5. Stel een digest-wachtwoord in, of schakel digest uit én configureer minimaal één toegestane IPv4/CIDR. Zonder deze beveiliging start de ontvanger niet.
6. Sla op. Test offline de uitgang met de 1 kHz-toon op −18 dBFS. Begin desgewenst met shared audio; exclusive valt met melding terug op shared als openen mislukt.
7. Configureer de firewall en forward UDP **5060**, **5004** en **5005** naar de studiopc. Zet SIP ALG aan de studiokant uit.
8. Klik **Ontvanger starten**. Bel vanuit Linphone of een Comrex in **EBU 3326/SIP**-modus naar `sip:studio@publiek-adres:5060`.
9. Controleer beide richtingen en de mix-minus, eerst buiten een uitzending. De ontvangen remote hoort niet in zijn eigen retour.

Het digest-wachtwoord wordt met DPAPI voor de huidige Windows-gebruiker opgeslagen; JSON bevat alleen een referentie. Dezelfde Windows-gebruiker moet de app later starten.

Instellingen en logs staan onder `%ProgramData%\DVC-it\SoftCodec`, of onder `%AppData%\DVC-it\SoftCodec` als de eerste map niet schrijfbaar is. Sluiten via het kruisje minimaliseert naar het systeemvak. Kies daar **Afsluiten** om echt te stoppen.

## Bouwen

Vereist: .NET 9 SDK, Windows voor uitvoeren van WPF/WASAPI. Kern en protocoltests kunnen ook op Linux worden gebouwd en uitgevoerd.

```powershell
dotnet restore SoftCodec.sln
dotnet build SoftCodec.sln -c Release
dotnet test tests/SoftCodec.Tests/SoftCodec.Tests.csproj -c Release
./build.ps1
```

`build.ps1` plaatst een self-contained Windows x64-build in `artifacts/publish`. Voor een klassieke installer: installeer Inno Setup 6, zet `ISCC.exe` op PATH en voer `./build.ps1 -Installer` uit. De installerbron staat in `installer/SoftCodec.iss`. De installer is in de cloudomgeving niet gecompileerd of uitgevoerd.

De meegeleverde GitHub Actions-workflow is broncode en is niet naar GitHub geüpload of daar uitgevoerd.

## Firewall en automatisch starten

Open een verhoogde PowerShell alleen voor firewallregels:

```powershell
./installer/firewall.ps1 -AppPath 'C:\Program Files\DVC-it\SoftCodec\SoftCodec.App.exe'
```

Deze helper opent alleen de opgegeven poorten voor Domain/Private. Bij gewijzigde apppoorten geef je `-SipPort`, `-RtpPort` en `-RtcpPort` mee. Public-profiel wordt niet automatisch geopend.

Automatisch starten configureer je vanuit de interactieve studio-account:

```powershell
./installer/autostart.ps1 -AppPath 'C:\Program Files\DVC-it\SoftCodec\SoftCodec.App.exe'
```

Dit registreert een taak bij aanmelden met herstart bij fouten. De app start geminimaliseerd; bij een bestaande geldige configuratie start de ontvanger automatisch. Het is geen Windows-audioservice. Verwijderen: hetzelfde script met `-Remove`.

## Architectuur

| Project | Verantwoordelijkheid |
|---|---|
| SoftCodec.Core | Publieke host- en PCM-contracten, instellingen, profielen, validatie, atomaire JSON-opslag, logging |
| SoftCodec.Sip | SIPSorcery UAS/UAC/registratie, SDP, digest en rate limiting, sessiecoördinatie |
| SoftCodec.Rtp | RTP, RTCP, statistieken, vaste/adaptieve jitterbuffer, driftcontroller, STUN |
| SoftCodec.Audio | Opus/G.722/G.711/L16/L24, streamingresampling, begrensde PCM-ringen, RTP-mediaketen, WAV24 |
| SoftCodec.Windows | WASAPI I/O, gescheiden capture/render, kanaaltoewijzing, DPAPI |
| SoftCodec.App | WPF/MVVM, dashboard, instellingen, profielen, logs, systeemvak, diagnose |
| SoftCodec.Tests | Protocol-, codec-, opslag-, beveiligings- en UDP-integratietests |

Het extra project `SoftCodec.Windows` houdt NAudio/WASAPI buiten de portable mediakern. `EmbeddedAudioDevices` koppelt een `IPcmSink` voor het remote-kanaal en een **afzonderlijke** `IPcmSource` voor de mix-minus aan dezelfde host.

## Langere protocolduurtest

```powershell
$env:SOFTCODEC_SOAK_SECONDS='86400'
dotnet test tests/SoftCodec.Tests/SoftCodec.Tests.csproj -c Release --filter LongerCall
```

De test draait met gesimuleerde audioapparaten en echte lokale SIP/RTP/RTCP-sockets. Dit vervangt de 7-daagse duurtest op de Windows-studiopc en de luistertest niet. Voor 7 dagen: `604800` seconden.

## Ondersteunde grenzen

Eén actief gesprek, IPv4, SIP/UDP, RTP/AVP zonder versleuteling. Opus, G.722 (8 kHz RTP-klok / 16 kHz audio), PCMA, PCMU, L16 32/48 kHz en L24 48 kHz. Symmetrische codec per gesprek. Alleen actieve `sendrecv`-audio; SIP-hold, fax en video worden niet aangeboden. Wijzigingen in instellingen vereisen stoppen van de ontvanger.

Geen BRIC Normal, CrossLock, Switchboard, AAC, MPEG Layer II, ASIO, TLS/SRTP, IPv6 of WebRTC. Geen volledige EBU-certificatieclaim. Niet alle optionele uitbreidingen uit het ontwerp zijn voltooid; zie de statusmatrix.

Bij elke distributie: lees `THIRD-PARTY-NOTICES.md` en de meegeleverde upstreamlicenties. SIPSorcery heeft aanvullende voorwaarden die niet gelijk zijn aan uitsluitend BSD-3-Clause.
