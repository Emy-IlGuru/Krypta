# ADR-0007: Accesso su approvazione, con sala d'attesa

**Data:** 2026-09-07
**Stato:** accettata — **modifica una premessa fondante**

## Contesto

Fino a questo punto Krypta aveva accesso libero: **conoscere l'ID a 6 caratteri era
sufficiente per entrare**. Tutta la protezione stava nel non far conoscere l'ID a chi non
doveva averlo, sostenuta da uno spazio ampio (36⁶) e da un limite sui tentativi.

Quella impostazione aveva due debolezze che nessuna delle mitigazioni previste chiudeva:

- **L'intrusione era silenziosa.** Chi indovinava un ID entrava e leggeva tutto; restando
  in silenzio non compariva da nessuna parte, perché un partecipante si conosce solo
  quando scrive o quando qualcuno digita `/Users`.
- **Un ID scelto da una persona non è casuale.** `RIUNIO`, `TEST01`, `123456` cadono con
  un dizionario di poche migliaia di tentativi, quale che sia l'ampiezza dello spazio
  teorico.

La soluzione che avevamo in programma per la fase 2 era la **password di stanza**: un
secondo segreto da comunicare insieme all'ID.

## Opzioni valutate

**1. Password di stanza.** Chiude il problema: conoscere l'ID non basta. Ma introduce un
secondo segreto da concordare e ricordare, e sposta soltanto la domanda — chi ha la
password entra, e nessuno se ne accorge. Resta un controllo *statico*: si decide una volta
sola, all'inizio, per chiunque arriverà.

**2. Invito con token monouso.** Ogni ingresso richiede un token generato da chi è dentro.
Molto solido, ma richiede un canale per distribuire i token e una gestione della loro
scadenza: complicato per una chat che dovrebbe iniziare in trenta secondi.

**3. Sala d'attesa con approvazione.** Chi arriva su una stanza esistente resta fuori
finché un partecipante non lo accetta. Il controllo diventa *dinamico*: si decide caso per
caso, sapendo chi sta bussando e quando.

## Decisione

**Opzione 3.** Chi chiede di entrare in una stanza esistente finisce in sala d'attesa;
qualunque partecipante può accettarlo o rifiutarlo.

Le regole che la definiscono:

- **Basta un solo `approve`.** Nessuna maggioranza, nessuna unanimità, nessun ruolo — resta
  vero che la stanza non ha un proprietario ([ADR-0005](0005-comandi-di-stanza-senza-ruoli.md)).
  Il contrappeso è lo stesso di sempre: **la decisione è pubblica**, il messaggio di sistema
  dice chi ha accettato o rifiutato.
- **Il primo che arriva entra senza approvazione.** Non c'è alternativa: non c'è ancora
  nessuno dentro. Ne consegue che chi vuole una stanza protetta deve crearla per primo.
- **Chi attende non riceve nulla della stanza:** né nome, né numero di partecipanti, né
  messaggi. È il dettaglio che trasforma la sala d'attesa da ostacolo a difesa — bussare a
  un ID non rivela più nemmeno se quella stanza esiste ed è affollata.
- **Le richieste scadono da sole dopo 120 secondi**, e la coda è limitata a 10.

## Conseguenze

**Più facile — ed è il punto:** l'intrusione silenziosa scompare. Chi indovina un ID non
ottiene più un accesso, ottiene una richiesta visibile e rifiutabile. È un cambio di
categoria del rischio, non un miglioramento graduale, e riordina le difese:
la sala d'attesa diventa la prima, il rate limiting la seconda, l'ampiezza dello spazio
degli ID la terza ([07 §4](../07-sicurezza-e-privacy.md)).

**La password di stanza esce dai piani.** Serviva esattamente a impedire che conoscere
l'ID bastasse per entrare, e l'approvazione lo fa senza un secondo segreto da comunicare.
Non la escludo per sempre, ma non ha più una ragione autonoma di esistere.

**Più difficile:** entrare non è più immediato. Serve qualcuno dentro che stia guardando,
e per una chat "usa e getta" è attrito vero — chi arriva quando gli altri non guardano lo
schermo aspetta due minuti e viene respinto da un timeout. È il costo dichiarato di questa
scelta.

Cresce anche la superficie del client e del protocollo: uno stato in più della connessione
(*in attesa*), tre messaggi nuovi, una schermata e un pannello. E la sala d'attesa diventa
un nuovo bersaglio: bussare ripetutamente genera righe e notifiche per tutti. Il tetto di
10 richieste, il rate limiting sui join e il pannello che **non si apre da solo** sono le
mitigazioni; non azzerano il fastidio.

**Il limite da non travisare:** chi approva vede **un nickname e nient'altro**. Non può
verificare che dietro `marco` ci sia il Marco atteso. La barriera passa da "chi indovina
l'ID entra" a "chi indovina l'ID deve convincere qualcuno" — che è molto, ma **non è
autenticazione** e non va descritta come tale, né nella UI né nel materiale di progetto.

**Da rivedere se:** l'attrito si rivela insostenibile nell'uso reale. La via d'uscita meno
invasiva non è togliere l'approvazione, ma renderla saltabile per chi arriva con un
riferimento verificabile — cioè tornare all'opzione 2, il token d'invito, come *aggiunta*
e non come sostituzione.
