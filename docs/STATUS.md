# Implementatiestatus en vrijgavepunten

Datum: 10 november 2026. Versie: 0.1.0, ontwikkel- en testbuild.

## Aanwezig in de broncode

- C#/.NET 9-solution, portable mediakern, aparte Windows/WPF-laag, centraal vastgezette packages.
- Eén gesprek tegelijk, UAS/UAC via SIPSorcery, INVITE/ACK/BYE/CANCEL/OPTIONS, optionele externe registrar met bibliotheekverversing en proxy.
- SDP: één symmetrisch audioformaat in antwoord, codecprioriteit, G.722-klok, statische payloadtypes zonder rtpmap, onbekende attributen negeren, Opus-fmtp filteren.
- Re-INVITE: ongewijzigde sessieverversing; gewijzigde codec/endpoint wordt voorbereid en pas na ACK toegepast. Hierbij wordt de audioketen opnieuw geopend; klikvrije wisseling is niet bewezen.
- UDP RTP/RTCP, random sequence/timestamp/SSRC, symmetric RTP met bron- en SSRC-pinning, SR/rapportblok/SDES, jitter/loss/RTT.
- Begrensde en vooraf gealloceerde pakketring; vaste/adaptieve buffertiming, Opus PLC/FEC; niet-Opus packet-loss vervangt een ontbrekend frame door stilte.
- Opus, G.722, PCMA/PCMU, big-endian L16/L24, streamingresampling; gescheiden retourcapture.
- WASAPI exclusive/shared, raw en MMCSS-builderopties, audioapparaat- en kanaalkeuze, terugval naar shared met melding.
- Render-klokdriftcorrectie via PI-regelaar en resampler, begrensd op ±300 ppm. Dit is nog niet op echte deviceklokken gekalibreerd.
- WAV24-opname remote en optioneel retour via begrensde achtergrondqueue; fouten worden zichtbaar gemeld.
- Digest MD5 met qop=auth, gesigneerde/verlopende brongebonden nonces en nonce-count replaycontrole. IP/CIDR, rate limiting en scannerfilter; fail-closed bij ontbrekende beveiliging.
- JSON atomic replace met backup en herstel; DPAPI CurrentUser; Serilog met dag-/grootterotatie, 14 bestanden retentie; begrensde logviewer.
- Dashboard met RMS-niveaus, duur en metriek, buffergrafiek, aannemen/weigeren/ophangen/uitbellen/retour dempen.
- Instellingen, contacten, profielkeuze op bron-IP/From, offline testtoon, STUN-detectie, stilte-/mediafoutmelding, diagnosezip, always-on-top en systeemvak.
- Inno Setup-bron, firewall- en Taakplannerhelpers, buildscript en CI-workflowbron.
- Inbeddingsadapter voor de mixer van REA-Radio-DJ; daadwerkelijke wijzigingen in die afzonderlijke applicatie zijn niet gemaakt.

## Belangrijke verschillen en nog open werk

| Onderdeel | Status / resterend werk |
|---|---|
| Windows UI en audio | Cross-build beschikbaar. Niet gestart of beluisterd op Windows; formaten, raw/exclusive, channel mapping, apparaatverlies en USB-herstel op studiopc testen. |
| Comrex | Geen hardware beschikbaar. Werkelijke SIP/SDP, firmware, L24, WAN/NAT en FEC controleren met PCAP en luistertest. |
| Jitter en drift | Basisalgoritmen aanwezig en unit-getest. Adaptieve wijzigingen kunnen korte gaten geven; regelaar en time-stretch voor uitzendkwaliteit verder meten en afregelen. Geen goedgekeurde 24/7-kwaliteitclaim. |
| Audioallocaties / opname | Opnamequeue kopieert PCM-blokken en heeft per-blok allocaties. GC, diskstall, renderunderruns en performance met echte apparaten meten en verbeteren voor productie. |
| Opnamebestanden | Klassiek RIFF/WAV stopt met fout vóór 4 GB. Bestandsrotatie/RF64 en crashbestendige tussentijdse headers ontbreken. Opname aan/uit wordt nu vóór het gesprek ingesteld; geen live recordknop. |
| Profielen | Basisscherm en codeconderhandeling aanwezig. Geavanceerde jitter-/Opus-overrides zijn model/API-velden, niet alle zijn bewerkbaar via het profielscherm. |
| Meters | Afzonderlijke L/R-RMS-meters op een hardwareachtig frontpaneel; peak-hold en gekalibreerde VU-ballistics ontbreken nog. |
| Alarmacties | Visueel en log. Configureerbare akoestische alarmacties en webhook ontbreken. |
| Diagnose | ZIP met recente logs en configuratie, zonder secretdirectory. Adressen, namen en bestemmingen staan wel in het pakket; controleer vóór delen. Uitgebreide automatische anonimisering ontbreekt. |
| Session timers | RFC 4028 niet geïmplementeerd; niet-nul configuratie wordt expliciet afgewezen. Normale gesprekken zonder session timers werken. |
| Service/watchdog | Taakplannerhelper aanwezig; apart watchdogproces niet gebouwd. Warm-open apparaten tussen gesprekken niet geïmplementeerd. |
| Installer | Inno Setup-bron aanwezig, niet gecompileerd/getest; geen code signing, automatische updates of uninstall-behoudtest. |
| Generic Host / DI | WPF-start/stop via Microsoft.Extensions.Hosting; viewmodel en hoofdvenster via DI. Hardwarediensten worden nog expliciet door het viewmodel samengesteld. |
| Logging | Serilog en viewer aanwezig; logging van de interne SIPSorcery-bibliotheek is nog niet aan dezelfde UI-sink gekoppeld. |
| Latency en statistiek | RTT uit RTCP, lokale buffers/ptime indicatief; bitrate is gespreksgemiddelde, RTCP-lossfractie cumulatief. Geen nauwkeurige gemeten end-to-end latency. |
| Licenties | NuGet-metadata en beschikbare upstreamlicentiebestanden bijgevoegd; juridische beoordeling en volledige distributie-clearance zijn niet uitgevoerd. |
| REA-Radio-DJ | Herbruikbare interfaces/adapter klaar; geen toegang tot dat project, dus nog geen in-process integratie. |
| Vrijgave | Nog Windows-LAN/WAN-tests, security-fuzz op SIP-stack, clumsy-suite, 7×24 uur soak, hardwareattest en subjectieve luistertest. |

Deze punten zijn geen bevestigde productiemogelijkheden. Het ontwerp blijft de doelstelling; deze build implementeert een grote eerste verticale doorsnede en is niet gelijk aan de volledige afgeronde v1-roadmap.
