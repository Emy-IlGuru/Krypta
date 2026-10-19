# ADR-0003: Colori dei nickname deterministici lato client

**Data:** 2026-09-06
**Stato:** accettata

## Contesto

Ogni utente deve avere un colore automatico per il proprio nome, in stile Discord.
La domanda è **chi assegna il colore**.

Il vincolo che decide tutto: due client nella stessa stanza devono vedere `emy` dello
**stesso** colore. Se ognuno assegna a modo suo, il colore smette di essere un aiuto e
diventa una fonte di confusione ("il rosso ha detto che…" — ma per l'altro è verde).

## Opzioni valutate

**1. Il server assegna il colore all'ingresso** e lo comunica ai client.
Garantisce coerenza e permetterebbe di evitare le collisioni scegliendo un colore libero.
Ma richiede che il server tenga uno stato in più per partecipante, e soprattutto che i
colori viaggino nel protocollo: informazione che riguarda solo la presentazione.

**2. Ogni client assegna a caso** al primo avvistamento di un nickname.
Nessun costo, nessuna coerenza. Scartata.

**3. Funzione deterministica del nickname**, calcolata identicamente da ogni client.
Stesso nome, stesso colore, ovunque, senza scambiare un byte.

## Decisione

**Opzione 3: hash FNV-1a a 32 bit del nickname normalizzato, modulo una palette di 16
colori.** Nessun coinvolgimento del server.

Dettagli vincolanti ([05 §6](../05-client-desktop.md)):

- Normalizzazione con `Trim()` + `ToLowerInvariant()` prima dell'hash, così `Emy` e
  `emy` non ricevono colori diversi.
- **Non si usa `string.GetHashCode()`.** In .NET è randomizzato a ogni avvio del
  processo: darebbe colori diversi su client diversi, e persino tra due avvii dello
  stesso client. È l'errore che romperebbe silenziosamente l'intera decisione, e lo
  farebbe in modo difficile da notare in sviluppo — dove di solito si guarda un client
  solo.
- Palette di 16 colori con contrasto ≥ 4.5:1 su sfondo scuro.
- Il mittente riservato `STANZA` dei messaggi di sistema **non** usa la palette: ha un
  grigio fisso. Se passasse dalla stessa funzione, un messaggio di sistema sarebbe
  visivamente indistinguibile da un utente.

## Conseguenze

**Più facile:** il protocollo resta libero da dettagli di presentazione. Un tema chiaro
si aggiunge cambiando solo la palette del client. Il server non ha nulla da sapere sui
colori.

**Più difficile:** con 16 colori e fino a 20 partecipanti, **le collisioni sono
garantite**. Due utenti possono avere lo stesso colore. È accettabile: il colore aiuta a
scorrere la conversazione, non identifica nessuno — il nickname resta sempre scritto per
esteso accanto.

La collisione di colore è innocua però solo perché i nickname sono unici nella stanza
(invariante I6): due utenti che scelgono lo stesso nome diventano `emy` ed `emy2`, che
sono stringhe diverse e ricevono quindi **colori diversi**. La disambiguazione numerica
si vede anche dal colore, non solo dalla cifra finale.

Da qui una regola vincolante: il colore si calcola **sempre sul nickname effettivo**,
quello restituito dal server nel campo `nickname` di `joined` — mai su quello digitato
dall'utente. Un client che colorasse il proprio nome partendo dal testo digitato si
vedrebbe di un colore diverso da come lo vedono gli altri.

**Da rivedere se:** si aggiunge il tema chiaro (serve una seconda palette, indicizzata
allo stesso modo).
