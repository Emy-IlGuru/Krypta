# Architecture Decision Records

Una decisione importante = un file, numerato progressivamente: `NNNN-titolo-decisione.md`.
Un ADR non si modifica dopo essere stato accettato: se la decisione cambia, si scrive un
nuovo ADR che dichiara superato il precedente.

## Decisioni registrate

| # | Decisione | Stato |
|---|---|---|
| [0001](0001-trasporto-websocket.md) | WebSocket con payload JSON come trasporto | accettata |
| [0002](0002-nessuna-persistenza.md) | Nessuna persistenza dei messaggi | accettata |
| [0003](0003-colori-nickname-deterministici.md) | Colori dei nickname deterministici lato client | accettata |
| [0004](0004-id-stanza-alfanumerico.md) | ID stanza: 6 caratteri alfanumerici maiuscoli, come stringa | accettata — errata 2026-09-08, decisione invariata |
| [0005](0005-comandi-di-stanza-senza-ruoli.md) | Comandi di stanza, senza ruoli né proprietari | accettata |
| [0006](0006-partecipanti-visibili-nella-stanza.md) | I partecipanti sono visibili dentro la stanza | accettata — modifica una posizione precedente |
| [0007](0007-accesso-su-approvazione.md) | Accesso su approvazione, con sala d'attesa | accettata — modifica una premessa fondante |
| [0008](0008-cifratura-e2e-chiave-all-approvazione.md) | TLS ed E2EE obbligatori, chiave di stanza consegnata all'approvazione | accettata — sposta la cifratura nel perimetro obbligatorio |
| [0009](0009-runtime-dotnet-10.md) | Runtime .NET 10 (LTS) | accettata — sostituisce la decisione D8 |
| [0010](0010-messaggio-di-richiesta.md) | Messaggio allegato alla richiesta d'ingresso | accettata — estende ADR-0007 |
| [0011](0011-rotazione-chiave-di-stanza.md) | Rotazione della chiave di stanza (`/GenerateKey`) | accettata — chiude il limite 2 di ADR-0008 |

## Template

```markdown
# ADR-000N: Titolo della decisione

**Data:** AAAA-MM-GG
**Stato:** proposta | accettata | superata da ADR-XXXX

## Contesto
Qual è il problema, quali vincoli abbiamo, perché la decisione va presa ora.

## Opzioni valutate
1. Opzione A — cosa comporta, cosa costa
2. Opzione B — cosa comporta, cosa costa

## Decisione
Cosa abbiamo scelto e perché. Se un'opzione è stata scartata per un motivo
non ovvio, dirlo qui: è la parte che serve rileggere fra sei mesi.

## Conseguenze
Cosa diventa più facile, cosa più difficile, e sotto quali condizioni
questa decisione andrà rivista.
```
