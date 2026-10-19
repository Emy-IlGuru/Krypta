# ADR-0008: TLS ed E2EE, entrambi obbligatori — chiave di stanza consegnata all'approvazione

**Data:** 2026-09-07
**Stato:** accettata — **sposta la cifratura dentro il perimetro obbligatorio**

## Contesto

Fino a ieri la cifratura era rinviata: TLS in fase 2, E2EE in fase 3, con un'avvertenza in
testa al documento di sicurezza che diceva apertamente che **Krypta non proteggeva dal
traffico osservato sulla rete**, nonostante il nome.

Era una posizione difendibile per un MVP in LAN, ma fragile: un prodotto che si chiama
Krypta e non cifra niente ha un problema di sostanza, non di comunicazione.

La domanda di partenza era: *«il server crea una chiave per la cifratura dei messaggi per
ogni stanza, si può fare in SHA-256?»* Contiene due cose da separare.

**SHA-256 non può cifrare.** È una funzione di hash: a senso unico, senza chiave, senza
operazione inversa. Cifrare con SHA-256 renderebbe i messaggi illeggibili anche al
destinatario. Serve invece — ed è dove finisce nel design — dentro **HKDF**, per derivare
una chiave della lunghezza giusta da un segreto condiviso.

**Se la chiave la genera il server, il server legge tutto.** Si otterrebbe protezione
solo contro chi osserva la rete: esattamente ciò che dà TLS, con molto meno codice e
garanzie migliori. Sarebbe crittografia costosa che non protegge da nessuno di nuovo.

## Opzioni valutate

**A. Solo TLS (`wss://`).** Protegge dall'osservatore di rete. Il server continua a
leggere ogni messaggio. Costo: basso.

**B. E2EE con passphrase di stanza.** Chiave derivata con Argon2id da una parola d'ordine
condivisa fuori banda insieme all'ID. Il server non la vede mai. Costo: medio. Difetto:
introduce un **secondo segreto** da comunicare e ricordare — proprio ciò che si era appena
evitato scegliendo l'approvazione al posto della password di stanza
([ADR-0007](0007-accesso-su-approvazione.md)).

**C. E2EE con chiave consegnata al momento dell'approvazione.** Chi crea la stanza genera
una chiave casuale nel proprio client; chi accetta un nuovo arrivato gliela consegna
cifrata con la sua chiave pubblica. Il server inoltra involucri opachi. Costo: medio-alto.

**D. Double Ratchet / MLS.** Forward secrecy e post-compromise security reali. Richiede
identità persistenti per dispositivo, cioè l'opposto di questo prodotto.

## Decisione

**A e C insieme, entrambi obbligatori e dentro la fase 1.** Non sono alternative.

Il ragionamento è che coprono avversari diversi e nessuno dei due copre l'altro:

| | Chi osserva la rete | Chi gestisce il server |
|---|---|---|
| **Solo TLS** | protetto | **legge tutto** |
| **Solo E2EE** | **legge i metadati**: nickname, ID stanza, ingressi, uscite | protetto sui contenuti |
| **TLS + E2EE** | protetto | vede solo i metadati |

L'E2EE non nasconde i metadati perché **i messaggi di sistema li genera il server**, e un
server senza chiave non può produrre testo cifrato. Rinunciare a questo significherebbe
rinunciare alla sala d'attesa, a `/Users` e agli annunci di ingresso.

### Perché C e non B

Perché **l'approvazione è già il momento della fiducia**. Chi accetta un nuovo arrivato sta
compiendo esattamente il gesto che autorizza la lettura: consegnargli la chiave nello
stesso atto non aggiunge nessun passaggio per l'utente. La chiave viaggia in un campo che
il server inoltra senza poterlo aprire.

L'effetto pratico: **nessun secondo segreto da condividere.** Con B servirebbe concordare
ID *e* passphrase; con C basta l'ID, esattamente come oggi. La sala d'attesa decisa ieri
rende possibile un E2EE che ieri sarebbe stato scomodo — le due decisioni si sostengono a
vicenda, e questa è la ragione principale della scelta.

Ne discende anche una proprietà strutturale utile: **il server non può ammettere nessuno
da solo**, perché ogni ammissione richiede una chiave che il server non possiede. Un
`approve` senza `wrappedKey` è rifiutato.

### Primitive

ECDH su P-256, HKDF-SHA256, AES-256-GCM, tutte nella BCL: **nessuna dipendenza esterna sul
percorso delle chiavi**, coerente con [ADR-0001](0001-trasporto-websocket.md).

X25519 sarebbe preferibile ma non è nella BCL: richiederebbe libsodium o BouncyCastle. Il
formato della chiave pubblica è opaco al server, quindi la migrazione resterà possibile
senza toccarlo.

> *Nota del 2026-09-08.* Due precisazioni, nessuna delle quali cambia la decisione.
>
> **Verificato su .NET 10:** X25519 continua a non essere nella BCL
> ([ADR-0009](0009-runtime-dotnet-10.md)), quindi P-256 resta, e non era una scelta
> provvisoria.
>
> **"Opaco al server" non vuol dire "non specificato".** La frase qui sopra è vera e ha
> dato una falsa tranquillità: il formato è opaco al *server*, ma deve essere **identico
> fra i client**, altrimenti l'ECDH produce segreti diversi e i codici di verifica non
> coincidono mai — senza che nessuno riceva un errore. Le codifiche sono ora fissate byte
> per byte in [03 §7bis.9](../03-protocollo.md), con i vettori di test in §7bis.10.

L'AAD dei messaggi lega ciphertext, `roomId`, nickname del mittente e numero di sequenza:
**il server può cancellare o riordinare messaggi, ma non può falsificarne l'autore** senza
che la decifratura fallisca.

## Conseguenze

**Più facile:** l'avvertenza in testa al documento di sicurezza sparisce, e il nome del
prodotto smette di promettere qualcosa che non c'è. Cade anche il primo rischio in
roadmap.

**Più difficile — ed è sostanziale:**

- La **fase 1 raddoppia**. Per questo è divisa in 1A (nucleo in chiaro, verificabile da
  solo) e 1B (cifratura). 1A è un traguardo utile; **1B non è opzionale** e nessun uso
  reale è ammesso prima che sia completa.
- Il protocollo nasce cifrato: `krypta.v1` include già chiavi pubbliche e involucri. Era
  la ragione per farlo ora — aggiungerlo dopo avrebbe richiesto una migrazione.
- Il server perde capacità di validazione: non può più verificare che un messaggio non sia
  vuoto, né la lunghezza del testo in chiaro. Quei controlli restano solo lato client, e
  `EMPTY_MESSAGE` sparisce dal protocollo.
- Serve una gestione degli errori nuova: un messaggio che non decifra va mostrato come
  **non verificato**, mai come testo normale e mai scartato in silenzio.
- Il codice di verifica va spiegato all'utente, ed è l'unica difesa contro un server che
  sostituisca le chiavi pubbliche. È facoltativo, quasi nessuno lo userà, e va comunque
  reso disponibile.

**Cosa resta scoperto**, da non tacere in nessuna descrizione del prodotto: nessuna forward
secrecy; chi esce conserva la chiave; qualunque partecipante può cederla; chi crea la
stanza la genera, quindi un suo client compromesso compromette tutto; i metadati restano
visibili al gestore del server.

**Da rivedere se:** si accetta una dipendenza crittografica esterna (allora X25519 e
XChaCha20-Poly1305), o se serve forward secrecy reale (allora l'opzione D, che però
richiede identità persistenti e cambierebbe il prodotto).
