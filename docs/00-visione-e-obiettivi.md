# 00 — Visione e obiettivi

**Stato:** bozza · **Aggiornato:** 2026-09-08

## 1. In una frase

Krypta è una chat desktop a **stanze effimere**: entri con un ID di 6 caratteri, parli con
chi è collegato in quel momento, esci — e non resta traccia di niente, né sul server
né sui client.

## 2. Il problema

Le app di messaggistica di massa sono costruite attorno all'idea opposta: **conservare**.

- **WhatsApp / Telegram** ti legano a un numero di telefono. L'identità è permanente,
  la rubrica viene letta, la conversazione è archiviata per sempre e sincronizzata su
  più dispositivi. Anche Telegram, che è percepito come "privato", di default tiene
  tutto sui propri server.
- **Discord** ha le stanze, ma sono permanenti, pubbliche o semi-pubbliche, con storico
  infinito e lista membri visibile.
- **Signal** è tecnicamente eccellente ma resta legato al numero di telefono e allo
  storico locale sul dispositivo.

Manca uno strumento per la conversazione **temporanea e senza identità**: quella che
finisce quando finisce, e che non lascia un archivio da poter sequestrare, esportare
o consultare mesi dopo.

## 3. A chi ci rivolgiamo

Chi ha bisogno di parlare *ora* con un gruppo ristretto, senza creare un archivio:

- Piccoli team che vogliono un canale di coordinamento self-hosted, sotto il proprio
  controllo, senza dipendere da un servizio esterno.
- Chi ha bisogno di una conversazione "usa e getta" con persone con cui non vuole
  scambiare numero di telefono o account.
- Chi vuole poter ospitare il proprio server di chat in rete locale, senza che nulla
  esca dalla LAN.

**Utente tipo:** conosce cos'è un indirizzo IP e una porta, e sa che quel campo nella
prima schermata serve a qualcosa.

## 4. Le differenze

| # | Differenza | Descrizione | Priorità |
|---|---|---|---|
| 1 | **Nessuno storico** | Il server non memorizza i messaggi, nemmeno in RAM oltre l'istante dell'inoltro. Chi entra in una stanza non vede una riga di quello che è stato detto prima. | Must |
| 2 | **Nessuna registrazione** | Nessun numero di telefono, email, password o account. Solo un nickname, scelto ogni volta. | Must |
| 3 | **Stanze effimere** | La stanza nasce quando il primo client entra e viene distrutta quando esce l'ultimo. L'ID torna riutilizzabile. | Must |
| 4 | **Nessuna traccia dei partecipanti** | Dentro la stanza si vede chi c'è: ingressi e uscite sono annunciati e `/Users` elenca i presenti. Ma quell'elenco è costruito al momento, non è mai memorizzato, e sparisce con la stanza. La privacy sta nell'assenza di archivio, non nell'anonimato tra chi è già dentro. | Must |
| 5 | **Stanze non elencabili** | Non esiste una directory delle stanze. Rinominare una stanza non la rende trovabile: il nome è solo un'etichetta. | Must |
| 5b | **Accesso su approvazione** | Conoscere l'ID **non basta** per entrare. Chi arriva su una stanza esistente resta in sala d'attesa finché un partecipante non lo accetta, e può allegare un messaggio per spiegarsi. La richiesta è visibile a tutti: l'intrusione silenziosa non è possibile. | Must |
| 5c | **Il server non può leggere i messaggi** | La chiave di stanza nasce in un client e viene consegnata da un client, nel momento stesso dell'approvazione: il server inoltra involucri che non sa aprire. Nessuna passphrase in più da condividere — l'unica cosa da scambiare resta l'ID. | Must |
| 6 | **Server scelto dall'utente** | La prima schermata chiede IP e porta. Non c'è un server ufficiale a cui si è obbligati a connettersi. | Must |
| 7 | **Solo desktop** | Nessuna app mobile: niente rubrica del telefono, niente notifiche push, niente identificatore di dispositivo. | Must |

## 5. Cosa NON facciamo

Confini espliciti dell'MVP. Ognuno di questi è una scelta, non una mancanza:

- ❌ Storico dei messaggi, backup, export
- ❌ Account persistenti, password, recupero identità
- ❌ Ricerca nei messaggi
- ❌ Allegati: immagini, file, audio, video
- ❌ Chiamate vocali o video
- ❌ App mobile
- ❌ Notifiche push
- ❌ Moderazione, ruoli, amministratori, ban
- ❌ Directory pubblica delle stanze
- ❌ Reazioni, thread, risposte, modifica messaggi
- ❌ Messaggi diretti fuori dalle stanze

Nella stanza esistono cinque comandi — `/NameRoom`, `/Users`, `/ClearRoom`, `/GenerateKey`
e `/Help` —
a disposizione di **tutti** i partecipanti: non c'è nessun proprietario della stanza.
Il contrappeso non è un permesso, è la tracciabilità: ogni comando che cambia qualcosa
annuncia chi lo ha eseguito. `/Help` è l'unico che non passa dal server — la risposta la
vede solo chi lo digita.

## 6. Come si misura il successo (MVP)

L'MVP è riuscito se:

1. Due client su macchine diverse della stessa LAN entrano nella stessa stanza
   inserendo lo stesso ID, e si scambiano messaggi in tempo reale.
2. Un terzo client che entra dopo **non vede** nessun messaggio precedente.
3. Quando l'ultimo client esce, la stanza sparisce dal server: rientrando con lo stesso
   ID si ottiene una stanza nuova e vuota.
4. Il numero di partecipanti mostrato in alto è sempre corretto entro un secondo da
   ogni ingresso o uscita.
5. Una stanza regge 20 utenti simultanei senza ritardi percepibili.
