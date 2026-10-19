# ADR-0011: Rotazione della chiave di stanza (`/GenerateKey`)

**Data:** 2026-09-08
**Stato:** accettata — chiude il limite 2 di [ADR-0008](0008-cifratura-e2e-chiave-all-approvazione.md)

## Contesto

[ADR-0008](0008-cifratura-e2e-chiave-all-approvazione.md) elenca fra i propri limiti
dichiarati:

> **Chi esce conserva la chiave.** Non c'è rotazione all'uscita di un partecipante: chi è
> stato dentro può decifrare il traffico successivo, se riesce a registrarlo.

È il limite più fastidioso dei cinque, perché è l'unico che l'uso normale incontra davvero:
una stanza dura ore, la gente entra ed esce, e la chiave nata al primo ingresso resta
valida per tutti quelli che l'hanno vista passare. Un comando che rigenera la chiave lo
chiude, e si incastra con il resto senza toccare nessun'altra decisione.

## Opzioni valutate

**A. Chiave nuova cifrata con la chiave vecchia.** Un solo involucro, trasmesso a tutta la
stanza: chi è dentro ha la chiave vecchia e apre. Circa trenta righe di specifica, nessuna
struttura dati nuova.

**B. Chiave nuova cifrata separatamente per ogni partecipante**, con la sua chiave pubblica
di sessione — la stessa meccanica dell'approvazione, ripetuta per ciascuno. Richiede che i
client conoscano le chiavi pubbliche dei presenti.

**C. Rotazione automatica a ogni `user_left`.** Nessun comando: il sistema si difende da
solo.

## Decisione

**Opzione B**, con il comando `/GenerateKey` eseguibile da qualunque partecipante.

### Perché non A, che è la parte che conta

Perché **A non protegge da chi dovrebbe escludere.** Chi possiede la chiave dell'epoca *N*
può decifrare il messaggio di rotazione che trasporta la chiave *N+1*, dato che quel
messaggio è cifrato proprio con la chiave *N*. E dalla *N+1* ricava la *N+2*. La catena si
segue per intero: **chi ha avuto una chiave qualsiasi le ha tutte**, per sempre.

E quella popolazione — "chi è stato dentro" — è esattamente quella che la rotazione esiste
per tagliare fuori. Sotto A l'unica cosa che glielo impedisce è **TLS**, che è un
meccanismo diverso, copre un avversario diverso, e che [03 §1.1](../03-protocollo.md)
dice esplicitamente di non far coprire da solo. Avremmo scritto "la rotazione esclude chi è
uscito" intendendo "purché non riesca a leggere un frame", cioè una di quelle promesse
attenuate che il progetto ha evitato ovunque.

Sotto B la rotazione **guarisce**: chi aveva la chiave vecchia non ha nessun modo di
ottenere la nuova, perché il suo involucro non esiste. È una proprietà, non una difficoltà.

### Perché non C

L'automatismo sembra più sicuro e sarebbe peggio. Una stanza dove qualcuno entra ed esce di
continuo rigenererebbe la chiave a raffica, con venti involucri per volta e una riga di
sistema ogni pochi secondi; e un partecipante che si scollega e riconnette in continuazione
diventerebbe un modo per tenere occupata la stanza. Resta possibile aggiungerla dopo, sopra
lo stesso meccanismo: la decisione qui è il meccanismo, non chi lo innesca.

### Come funziona

1. Il server **conserva la `publicKey`** che ogni partecipante ha inviato nel `join`. Oggi
   la usa solo per inoltrarla nel `waiting`; ora resta associata al partecipante finché è
   nella stanza.
2. Il server invia ai partecipanti un **`roster`** con le coppie `{nickname, publicKey}`, a
   ogni cambio di composizione.
3. `/GenerateKey` fa generare al client 32 byte nuovi, che cifra **una volta per ciascun
   altro partecipante**, e invia in un unico messaggio `rekey`.
4. Il server verifica che i destinatari siano **esattamente** i partecipanti attuali meno
   il mittente, poi consegna a ognuno il proprio involucro e annuncia la rotazione.
5. Un `keyEpoch` sull'involucro dei messaggi dice con quale chiave sono stati cifrati.

### Le tre scelte non ovvie dentro il meccanismo

**L'elenco dei destinatari deve essere completo, e il server lo verifica.** Non è pignoleria:
se una rotazione potesse omettere qualcuno, sarebbe un modo per **espellere un partecipante**
lasciandolo nella stanza ma senza chiave — un *kick* dalla porta di servizio, in un prodotto
che ha deciso di non avere ruoli né poteri ([ADR-0005](0005-comandi-di-stanza-senza-ruoli.md)).
Il server non può leggere gli involucri, ma può contarli e verificarne i destinatari, ed è
sufficiente.

**Il `keyEpoch` non entra nell'AAD.** Sembrerebbe naturale legarcelo, ma non serve: l'epoca
seleziona la chiave, quindi un'epoca falsificata porta a decifrare con la chiave sbagliata e
il tag GCM fallisce da solo. È già autenticata implicitamente. Tenerla fuori dall'AAD ha un
vantaggio concreto: **i sei vettori di test di [03 §7bis.10](../03-protocollo.md) restano
validi**, e non vanno rigenerati.

**Il ricevente tiene la chiave precedente per 60 secondi.** Durante una rotazione ci sono
messaggi in volo cifrati con la chiave vecchia. Senza la finestra apparirebbero tutti come
*non verificati* — un allarme di sicurezza a ogni rotazione, per un motivo che non è un
attacco. È lo stesso errore già evitato con il contatore `seq` al riuso di un nickname
([03 §7bis.5](../03-protocollo.md)).

## Conseguenze

**Chiude il limite 2 di ADR-0008**, e va tolto da lì: chi esce conserva la chiave solo fino
alla prima rotazione.

**Non chiude gli altri.** Nessuna forward secrecy *dentro* un'epoca: chi ottiene la chiave
corrente legge tutto il traffico di quell'epoca che sia riuscito a registrare. E qualunque
partecipante può sempre cedere la chiave a mano.

**Riapre la finestra MITM a ogni rotazione.** L'esclusione di B è garantita *se le chiavi
pubbliche sono autentiche*; un server malevolo può sostituirle, esattamente come
all'approvazione ([03 §7bis.7](../03-protocollo.md)). Prima quella finestra si apriva una
volta per partecipante, all'ingresso; ora si riapre a ogni `/GenerateKey`. Il rimedio resta
il codice di verifica, e resta facoltativo — quindi in pratica la rotazione **aumenta** la
superficie verso un server ostile, mentre la riduce verso gli ex partecipanti. Sono due
avversari diversi, e vale la pena non confonderli.

**Il server conserva un dato in più:** la chiave pubblica di sessione di ogni partecipante,
per la durata della permanenza. È materiale pubblico e va comunque in giro nel `waiting`,
ma la tabella di [07 §7](../07-sicurezza-e-privacy.md) va aggiornata: la regola del progetto
è che ciò che il server tiene si scrive.

**Da rivedere se:** l'uso reale mostra che nessuno digita `/GenerateKey` quando servirebbe.
La via d'uscita è l'opzione C — rotazione automatica all'uscita di un partecipante — che
riusa questo stesso meccanismo e cambia solo chi lo innesca.
