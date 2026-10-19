# ADR-0002: Nessuna persistenza dei messaggi

**Data:** 2026-09-06
**Stato:** accettata

## Contesto

Ogni app di messaggistica conosciuta conserva i messaggi: sul server, sul client, o su
entrambi. È l'assunzione di default del dominio. Krypta la rifiuta esplicitamente:
"non ci sarà uno storico, ma chi è in ascolto riceve i messaggi".

Questa decisione va registrata perché è **la** decisione del progetto: quasi tutto il
resto dell'architettura ne discende.

## Opzioni valutate

**1. Persistenza completa** — database dei messaggi, storico su richiesta.
È il modello di WhatsApp e Telegram. Fuori discussione: contraddice il prodotto.

**2. Buffer breve in memoria** — il server tiene gli ultimi N messaggi e li invia a chi
entra. Migliora molto l'esperienza di chi arriva a metà conversazione.
Ma crea una finestra in cui i messaggi *esistono* da qualche parte: recuperabili da chi
entra dopo, e presenti in un eventuale dump di memoria. La promessa "chi entra non vede
il prima" diventerebbe falsa.

**3. Nessuna persistenza.** Il messaggio esiste per la durata di una chiamata di metodo.

## Decisione

**Nessuna persistenza, in nessun punto del sistema.**

- Il server non scrive su disco, non tiene code, non ha buffer di replay.
- Il client non scrive su disco. La cronologia vive nella `ObservableCollection` della UI
  e muore alla chiusura della finestra.
- Nemmeno i log contengono contenuti di messaggi ([04 §6](../04-api-server.md)).

Il buffer breve dell'opzione 2 è stato scartato consapevolmente: **è la stessa
funzionalità dello storico, solo più corta.** Una volta ammesso il principio "il server
può tenere gli ultimi 10 messaggi", la domanda diventa perché non 100, e la garanzia
sparisce.

## Conseguenze

**Più facile:** niente database, niente migrazioni, niente backup, niente cifratura a
riposo, niente policy di retention, niente da consegnare in risposta a un ordine
giudiziario. Il diritto alla cancellazione è soddisfatto per costruzione. Il server è un
singolo processo senza stato durevole: riavviarlo non perde nulla che dovesse
sopravvivere.

**Più difficile:** chi entra a metà conversazione non ha contesto. Chi perde la
connessione per dieci secondi perde quei dieci secondi di conversazione, definitivamente.
Non esiste multi-dispositivo, né notifica di messaggi persi. Non è possibile alcuna
moderazione a posteriori.

**Da rivedere se:** mai, senza prima modificare [00 — Visione](../00-visione-e-obiettivi.md).
La regola 7 di [07 §9](../07-sicurezza-e-privacy.md) esiste apposta: qualsiasi richiesta
di "conservare solo un po'" è una modifica al prodotto, non un dettaglio implementativo.
