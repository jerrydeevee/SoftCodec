# Bouw- en testresultaten — 10 november 2026

- Omgeving: Linux x64, .NET SDK 9.0.321 / runtime 9.0.23.
- De volledige solution en WPF-app zijn succesvol gecompileerd; geen compilerwaarschuwingen of fouten.
- 60 tests geslaagd, 0 mislukt, 0 overgeslagen in de volledige Release-run.
- Losse 60-seconden verbindingstest met echte lokale SIP/RTP/RTCP-sockets en gesimuleerde geluidskaarten geslaagd; audio bleef doorlopen, RTT uit RTCP beschikbaar.
- Zes codecvarianten op twee hosts: Opus, G.722, PCMA, PCMU, L16 en L24; signaal in beide richtingen, poorten vrij na ophangen.
- Re-INVITE Opus → PCMA: oorspronkelijke codec bleef actief vóór ACK, nieuwe codec pas daarna.
- Digest-authenticatie met echte SIP-challenge, replaycontrole en brongebonden nonce.
- Handmatig opnemen en weigeren getest; audioapparaten openen pas bij beantwoorden.
- RTP wrap/reordering/padding/extensions, 10.000 willekeurige malformed RTP-pakketten, RTCP, SDP-interoperabiliteitsgevallen, FEC-slotgedrag, adaptieve timing, driftgrenzen, PCM, resampling en WAV/JSON-herstel getest.
- Self-contained Windows x64-publicatie succesvol; SoftCodec.App.exe is een Windows PE32+ GUI-executable. De gehele map is nodig, niet alleen de exe.
- WPF is niet interactief uitgevoerd, WASAPI niet geopend, en Inno Setup niet gecompileerd op deze Linux-host. Comrex, Windows-WAN en de voorgeschreven 7-daagse duurtest blijven open.

Bewijs: `test-results/SoftCodec.trx`, volledige testrunlog, 60-secondenrunlog en publicatielog in het bronpakket. Een VSTest-adapterwaarschuwing over de tijdelijke artefactmap is geen compiler- of testfout.
