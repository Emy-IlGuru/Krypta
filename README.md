# Krypta

App di messaggistica desktop, **privata ed effimera**.

> Entri in una stanza con un ID di 6 caratteri, parli, esci. Non resta niente.

## Il principio

**Nessuno storico.** Un messaggio esiste solo nell'istante in cui il server lo inoltra
ai client connessi. Chi non è in ascolto in quel momento, quel messaggio non lo vedrà
mai — né subito, né dopo. Il server non lo scrive su disco, non lo tiene in coda, non
lo ripete.

Ne consegue tutto il resto: niente account, niente backup, niente ricerca, niente
moderazione a posteriori.

Dentro la stanza, invece, si sa chi c'è: ingressi e uscite sono annunciati e `/Users`
elenca i presenti. Krypta protegge dall'**archivio**, non dagli altri partecipanti —
quell'elenco è costruito al momento, non è memorizzato da nessuna parte e sparisce con
la stanza.

## L'accesso non è libero

Conoscere l'ID **non basta** per entrare. Chi arriva su una stanza che esiste già finisce
in **sala d'attesa**: non riceve nulla — né messaggi, né il nome della stanza, né il
numero di partecipanti — finché qualcuno già dentro non lo accetta. La richiesta è
visibile a tutti i partecipanti, e chi decide viene detto in chiaro.

Chi bussa può allegare un **messaggio** (*"Hey, mi vorrei unire alla stanza"*, modificabile)
che i partecipanti leggono dal loro pannello. Serve a spiegarsi, non a farsi riconoscere:
chi conosce l'ID può scrivere qualunque cosa, e va letto sapendolo. È anche l'unico testo
scritto da un utente che **non è cifrato** — chi lo scrive non ha ancora la chiave della
stanza.

Solo il **primo** che arriva su un ID libero entra senza approvazione: crea la stanza, e
non c'è ancora nessuno che possa approvarlo.

## Cifratura

Due livelli, entrambi obbligatori — coprono avversari diversi:

| Livello | Protegge | Da chi |
|---|---|---|
| **TLS** (`wss://`) | Tutto il traffico, metadati compresi | Chi osserva la rete |
| **E2EE** | Il **contenuto** dei messaggi | Chi gestisce il server |

La chiave di stanza nasce nel client che crea la stanza e viene consegnata a ogni nuovo
arrivato **dal client che lo accetta**: approvare qualcuno e dargli la chiave sono la
stessa operazione. Il server non la vede mai, e non può ammettere nessuno da solo.
Nessuna passphrase in più da concordare: si scambia solo l'ID.

**Cosa l'E2EE non copre:** nickname, ID stanza, ingressi, uscite e messaggi di sistema
restano visibili al gestore del server — è il server a generarli. Per questo TLS non è
opzionale. Dettagli in [03 §7bis](docs/03-protocollo.md).

> ⚠️ **Il nucleo server esiste, ma non esiste ancora una chat utilizzabile.** Mancano
> ancora eventi, comandi, client completo, TLS ed E2EE verificata. Finché la fase 1B non
> è completa, Krypta non protegge da nessuno dei due avversari. Non usarlo per niente di
> reale.

**Comandi di stanza** (a disposizione di tutti, nessun ruolo esiste):

| Comando | Effetto |
|---|---|
| `/NameRoom <nome>` | Rinomina la stanza (max 25 caratteri, lettere numeri e spazi). Default alla nascita: `RM-<id>` |
| `/Users` | Elenca i partecipanti, **a tutti** |
| `/ClearRoom` | Svuota la chat sullo schermo di tutti |
| `/GenerateKey` | Rigenera la chiave della stanza: **chi è già uscito non legge più**. Non caccia nessuno di chi è dentro |
| `/Help` | Elenca i comandi — **solo a chi lo digita**, non passa dal server |

## I due progetti

| Progetto | Cos'è | Tecnologia |
|---|---|---|
| **Krypta.Server** | L'API in ascolto: crea le stanze su richiesta e inoltra i messaggi | .NET 10 · ASP.NET Core · WebSocket |
| **Krypta.Client** | Il client desktop | .NET 10 · Avalonia 11 (Windows / Linux / macOS) |

Per ora tutto gira **in locale** (`127.0.0.1`).

## Documentazione

> Se lavori con un assistente AI, il punto di partenza è **[Context.MD](Context.MD)**:
> stato dei lavori, stack, regole non negoziabili e trappole note, con i puntatori qui
> sotto. Va letto prima di scrivere codice e aggiornato dopo.

| Documento | Contenuto |
|---|---|
| [00 — Visione e obiettivi](docs/00-visione-e-obiettivi.md) | Cos'è Krypta, per chi, in cosa differisce |
| [01 — Requisiti](docs/01-requisiti.md) | Requisiti funzionali e non funzionali |
| [02 — Architettura](docs/02-architettura.md) | Componenti, struttura della solution, stack |
| [03 — Protocollo](docs/03-protocollo.md) | **Il contratto tra client e server.** Messaggi JSON, errori, ciclo di vita |
| [04 — Server (API)](docs/04-api-server.md) | Specifica del server: stanze, limiti, concorrenza |
| [05 — Client desktop](docs/05-client-desktop.md) | Le 4 schermate, layout, colori dei nickname |
| [06 — Configurazione](docs/06-configurazione.md) | Il file `krypta.config.xml` |
| [07 — Sicurezza e privacy](docs/07-sicurezza-e-privacy.md) | Modello di minaccia, cosa proteggiamo e cosa no |
| [Mockup dell'interfaccia](docs/assets/mockup-interfaccia.html) | Le cinque schermate nei due temi, con le palette dei nickname. Da aprire nel browser |
| [adr/](docs/adr/) | Decisioni tecniche registrate |

## Convenzioni

- Documenti in Markdown, in italiano.
- Ogni documento riporta in testa **stato** (`bozza` / `in revisione` / `approvato`) e data.
- Ogni decisione tecnica importante diventa un ADR numerato in `docs/adr/`.
- Il documento **03 — Protocollo** è normativo: se client e server sono in disaccordo,
  vince quello che c'è scritto lì.
