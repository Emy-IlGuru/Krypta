# ADR-0001: WebSocket come trasporto

**Data:** 2026-09-06
**Stato:** accettata

## Contesto

Krypta ha bisogno di consegna **push** dal server ai client: i messaggi arrivano quando
arrivano, senza che il client li chieda. Servono connessioni bidirezionali persistenti,
in .NET, tra un server ASP.NET Core e un client Avalonia.

## Opzioni valutate

**1. TCP grezzo con protocollo custom**
Massimo controllo e overhead minimo, ma bisogna scrivere a mano framing, heartbeat e
gestione della chiusura. Ogni riga di quel codice è una riga in cui sbagliare.

**2. SignalR**
Astrae tutto: gruppi (che mappano perfettamente sulle stanze), riconnessione automatica,
client .NET di prima classe. In cambio si accetta un protocollo opaco sopra WebSocket e
una dipendenza che sta nel percorso di ogni messaggio.

**3. WebSocket con payload JSON**
Framing e ping/pong forniti dal protocollo, supporto nativo sia in ASP.NET Core
(`app.UseWebSockets()`) che nel client (`ClientWebSocket`), nessuna dipendenza esterna.
Il protocollo applicativo resta esplicito e ispezionabile.

## Decisione

**WebSocket con payload JSON**, sotto-protocollo `krypta.v1`.

Due motivi, in ordine:

1. **Il protocollo deve essere leggibile.** Per un prodotto la cui promessa è "non
   conserviamo niente", chi legge la documentazione deve poter verificare esattamente
   quali byte viaggiano. [03 — Protocollo](../03-protocollo.md) descrive ogni messaggio;
   con SignalR ci sarebbe un livello opaco in mezzo.
2. **Zero dipendenze sul percorso dei messaggi.** Meno codice di terze parti tocca il
   contenuto delle conversazioni, meno superficie da fidarsi.

Le comodità di SignalR che perdiamo — gruppi e riconnessione — costano rispettivamente
un dizionario concorrente (che serviva comunque per il ciclo di vita delle stanze) e una
funzionalità già classificata `Could` nell'MVP.

## Conseguenze

**Più facile:** ispezionare il traffico, scrivere client in altri linguaggi, cambiare
serializzazione, testare il protocollo in isolamento.

**Più difficile:** dobbiamo scrivere noi heartbeat applicativo ([03 §6](../03-protocollo.md))
e riconnessione. La serializzazione delle scritture per socket è a nostro carico
([04 §4.2](../04-api-server.md)).

**Da rivedere se:** servisse il fallback su long-polling per reti che bloccano i
WebSocket. Oggi, con esecuzione in LAN, non è un problema.
