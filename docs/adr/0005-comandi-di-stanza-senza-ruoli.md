# ADR-0005: Comandi di stanza, senza ruoli né proprietari

**Data:** 2026-09-06
**Stato:** accettata

## Contesto

Servono tre azioni dentro la stanza: rinominarla (`/NameRoom`), elencare i partecipanti
(`/Users`), svuotare la chat (`/ClearRoom`). Più un quarto comando, `/Help`, che esiste
solo perché gli altri tre non sarebbero altrimenti scopribili.

Due domande da decidere insieme: **come si invocano** e **chi può farlo**.

La seconda è quella difficile. Ogni app di messaggistica risolve il problema con i ruoli:
c'è chi ha creato il gruppo, chi è amministratore, chi non lo è. In Krypta quel modello
non è disponibile — e non per mancanza di tempo, ma per costruzione:

- non ci sono account, quindi non c'è a chi attribuire un ruolo che duri;
- la stanza è effimera, quindi non ha una storia in cui radicare una proprietà;
- chi "crea" la stanza è solo il primo arrivato su un ID libero, e può uscire dopo dieci
  secondi.

## Opzioni valutate

### Come si invocano

**1. Pulsanti nella UI.** Scopribili, ma occupano spazio nell'intestazione e vanno
disegnati e localizzati per ognuno.

**2. Comandi testuali con `/`.** Costo quasi nullo, convenzione già nota a chiunque abbia
usato IRC, Slack o Discord, estendibile senza toccare il layout. In cambio non sono
scopribili: se nessuno te li dice, non sai che esistono.

### Chi può eseguirli

**1. Tutti.** Semplice, senza stato aggiuntivo, coerente con l'assenza di account.
Esposto all'abuso: chiunque può svuotare la chat di tutti, e ripeterlo.

**2. Primo entrato ancora presente.** Un proprietario implicito che decade quando esce.
Non richiede account. Ma introduce uno stato di autorità in un sistema che non ne ha, con
una regola di successione da definire e da spiegare all'utente.

**3. Votazione dei partecipanti.** Sproporzionato per una chat da 20 persone.

## Decisione

**Comandi testuali, eseguibili da tutti i partecipanti.**

Sui comandi:

- Il nome è **insensibile a maiuscole**: `/NameRoom` e `/nameroom` sono equivalenti.
- `//` a inizio testo permette di scrivere un messaggio che inizia davvero con `/`.
- Il **client fa il parsing** e valida gli argomenti, così un comando scritto male non
  raggiunge nemmeno il server. Il **server rivalida sempre**: un client modificato invia
  quello che vuole.
- Un comando sconosciuto produce un messaggio **locale**, visibile solo a chi lo ha
  digitato.
- **`/Help` non raggiunge mai il server:** il client lo esegue da solo e mostra l'elenco
  dei comandi a chi lo ha digitato. Chiedere aiuto non deve comparire nella conversazione
  degli altri, e non deve consumare il limite di frequenza sui comandi. È la risposta al
  difetto principale dei comandi testuali — non essere scopribili.

Sull'autorizzazione, la scelta è **tracciabilità invece di permesso**:

- `/NameRoom` e `/ClearRoom` generano un messaggio di sistema che dice **chi** li ha
  eseguiti;
- `/Users` è trasmesso a **tutti**, non solo a chi lo ha chiesto: nessuno può controllare
  chi c'è di nascosto;
- il client chiede **conferma esplicita** prima di `/ClearRoom`, perché è distruttivo
  per gli altri e non solo per chi lo digita;
- un limite di 3 comandi ogni 10 secondi impedisce la ripetizione a raffica.

In un gruppo ristretto di persone che si conoscono — il caso d'uso previsto — sapere chi
è stato è sufficiente a regolare il comportamento. È lo stesso principio per cui la stanza
non ha una password: si assume che l'ID sia stato dato a persone di cui ci si fida.

## Conseguenze

**Più facile:** nessuno stato di autorità da mantenere, nessuna regola di successione
quando l'"amministratore" esce, nessuna UI di gestione permessi. Aggiungere un comando
costa una voce in un `switch` e una costante condivisa.

**Più difficile:** in una stanza con un estraneo, `/ClearRoom` ripetuto rende la
conversazione inutilizzabile e non c'è modo tecnico di impedirlo — l'unico rimedio è
cambiare stanza. È il prezzo dell'assenza di ruoli, ed è documentato in
[07 §4bis.2](../07-sicurezza-e-privacy.md).

Resta il difetto di fondo dei comandi testuali: bisogna sapere che esistono. `/Help` lo
attenua ma non lo elimina — chi non prova mai a digitare `/` non scoprirà nemmeno `/Help`.
Se emergesse che nessuno li usa, la risposta non è aggiungere comandi ma esporre le stesse
azioni anche come pulsanti nell'intestazione, tenendo i comandi come scorciatoia.

**Da rivedere se:** dall'uso reale emerge che l'abuso di `/ClearRoom` è un problema
concreto. La strada meno invasiva è l'opzione 2 — comandi che modificano lo stato
riservati al primo entrato ancora presente — che non richiede account e resta compatibile
con [ADR-0002](0002-nessuna-persistenza.md).
