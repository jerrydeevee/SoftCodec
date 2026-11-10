# Windows- en Comrex-acceptatie

Registreer per test datum, pc, Windows-build, interface/driver, Comrex-model/firmware, gebruikte codec/ptime/bitrate, netwerk en resultaat. Bewaar PCAPs apart; SIP kan identificatiegegevens bevatten.

1. Start/stop tienmaal; controleer vrijgave UDP-poorten en geluidskaart, geen achterblijvende processen.
2. Open de UI op 1280×720 en 1920×1080; controleer instellingen, wachtwoorden, profielen, systeemvak en afsluiten.
3. Offline testtoon naar gekozen uitgang. Controleer kanaaltoewijzing en −18 dBFS. Test exclusive bezet / shared fallback, 16/24/32-bit PCM en floatinterfaces.
4. Linphone LAN: Opus stereo/mono, G.722, PCMA/PCMU. Bij G.722 RTP-timestampstap 160 per 20 ms, audio 320 samples.
5. Comrex: expliciet EBU 3326/SIP kiezen, firmware noteren; automatisch/handmatig aannemen, weigeren, veld/studio ophangen, uitbellen, afgebroken opbouw.
6. Luister beide richtingen. Stuur verschillende testtonen in remote en retour; remote mag zijn eigen toon niet in retour krijgen. Test mute uitsluitend op retour.
7. Test re-INVITE met codec- en endpointwissel. Controleer PCAP: 200 + SDP en ACK vóór mediawisseling; geen tweede sessie of achterblijvende poorten.
8. WAN vanaf 4G: publiek SDP-adres, UDP-forward, SIP ALG uit, symmetric RTP. Test geen hairpin-NAT als bewijs van externe bereikbaarheid.
9. Digest: juist/onjuist wachtwoord, ontbrekende credentials, verlopen nonce, replay. Allowlist en INVITE-limiet. Niet-geautoriseerde media mag RTP-bron niet overnemen.
10. Laat media stoppen: melding en ophangen na timeout. Trek audio-interface los en herstel; beoordeel foutmelding en herstel.
11. Meet packetloss/jitter/RTT, Opus PLC/FEC, toenemende/afnemende buffer en apparaatklokdrift. Injecteer netwerkverlies/jitter/reordering met clumsy. Beluister stem én muziek.
12. Neem een lang gesprek op; controleer WAV-header, 24-bit, 48 kHz, beide stromen gescheiden en begrensde diskstall-fout. Houd rekening met 4 GB-bestandslimiet.
13. Test installer, firewall, normale gebruikersrechten, Taakplanner-herstart en verwijderen met behoud van instellingen.
14. Draai een 7×24-uurtest op echte Windows-hardware. Meet RSS, CPU, GC, renderunderruns, loss, buffer, drift, hoorbare clicks en vertraagde audio.
15. Pas na alle passende tests en licentiebeoordeling een productieversie vrijgeven.
