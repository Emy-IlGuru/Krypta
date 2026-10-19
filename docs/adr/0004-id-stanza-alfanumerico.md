# ADR-0004: ID stanza — 6 caratteri alfanumerici maiuscoli, come stringa

**Data:** 2026-09-06
**Stato:** accettata · **errata corrige 2026-09-08** (due formulazioni che contraddicevano
la decisione presa in questo stesso ADR; la decisione non cambia)

## Contesto

L'ID della stanza è l'unico modo per entrarci: si concorda fuori banda — a voce, per SMS,
su un foglio — e si digita. Non esiste directory, non esiste ricerca, il nome della stanza
non aiuta a trovarla.

La prima specifica diceva "un numero di 6 cifre". È stata poi estesa a **6 caratteri
alfanumerici, senza spazi né caratteri speciali**.

Tre dettagli non ovvi vanno decisi: il **tipo** con cui si rappresenta, il trattamento
delle **maiuscole**, e cosa comporta l'ampiezza dello **spazio** risultante.

## Decisione

### Formato: 6 caratteri alfanumerici ASCII, `[A-Z0-9]{6}`

Sono validi ID di sole cifre (`482913`), di sole lettere (`RIUNIO`) e misti (`K7X2M9`).
Sono rifiutati spazi, punteggiatura, accenti e qualunque cosa non sia una lettera ASCII
o una cifra — **e le minuscole**, per la regola della sezione successiva.

> *Errata 2026-09-08:* questo titolo diceva `[A-Za-z0-9]`, che è l'alfabeto dei simboli
> ammessi *prima* della regola sulle maiuscole e si leggeva come se `k7x2m9` fosse valido.
> La forma canonica, decisa qui sotto, è una sola.

### Tipo: stringa, mai un numero

Anche un ID di sole cifre resta una `string`. Trattarlo come intero perderebbe gli zeri
iniziali: `000042` diventerebbe `42` e, riformattato, una stanza diversa da quella
digitata. È un identificatore, non una quantità: non ci si fa aritmetica sopra.

Il test `RoomIdConZeriIniziali_Preservato` esiste apposta, perché è il tipo di bug che
non si manifesta finché qualcuno non sceglie proprio un ID che inizia per zero.

### Maiuscole: una sola forma valida, `[A-Z0-9]{6}`

**L'ID è sempre maiuscolo.** Non esiste una forma minuscola: il server rifiuta `k7x2m9`
con `INVALID_ROOM_ID` esattamente come rifiuterebbe `k7x2m` o `k7 x2m9`. È **il client**
a garantire la forma corretta, convertendo il testo mentre l'utente digita.

Erano tre le opzioni:

| | Approccio | Esito con `k7x2m9` |
|---|---|---|
| A | Sensibile al caso: `k7x2m9` ≠ `K7X2M9` | **Stanza nuova e vuota** |
| B | Tollerante: il server normalizza | Entra nella stanza giusta |
| C | **Una sola forma valida, il server rifiuta** | Errore esplicito — ma il client converte prima, quindi non capita |

**A è la più pericolosa**, e non per il motivo che sembra. Sbagliare il caso non darebbe
"stanza non trovata": in Krypta ogni ID libero è valido, quindi creerebbe una **stanza
nuova e vuota**. L'utente si ritroverebbe da solo credendo di essere nel posto giusto, e
gli altri non lo vedrebbero mai arrivare. Nessun messaggio, nessun sintomo: il fallimento
peggiore possibile per questo prodotto.

Il canale con cui l'ID viaggia è del resto umano, e non trasporta il caso: "kappa sette
ics due emme nove" non dice se le lettere sono maiuscole; chi lo scrive su un foglio usa
lo stampatello; chi lo copia da un telefono se lo vede autocapitalizzare.

Fra B e C abbiamo scelto **C**. Entrambe eliminano il fallimento silenzioso, ma C rende
il contratto non ambiguo: c'è una forma sola, quella memorizzata come chiave è identica a
quella restituita al client, e non esiste il rischio che una normalizzazione applicata in
un punto e dimenticata in un altro spacchi una stanza in due. Un client scritto male
riceve un errore chiaro invece di funzionare "quasi".

Il costo di C — un errore per chi digita minuscolo — è pagato dal client, che converte
durante la digitazione: l'utente batte `k7x2m9`, vede comparire `K7X2M9`, e la regola non
la incontra mai.

**Sullo spazio degli ID questa scelta non pesa.** Il caso sensibile porterebbe da 36⁶ a
62⁶ (≈ 56,8 miliardi, 26 volte tanto), ma 2,18 miliardi bastano già ampiamente: il
fattore limitante non è la dimensione dello spazio, è la **prevedibilità degli ID che le
persone scelgono**. Ventisei volte più combinazioni non proteggono chi digita `TEST01`.

### L'ID lo sceglie l'utente

Il server non genera ID nell'MVP: **valida e rifiuta, senza normalizzare nulla**. Deriva
direttamente dal requisito — si concorda un codice fuori banda e lo si digita. La
conversione in maiuscolo è del client, mentre l'utente digita.

> *Errata 2026-09-08:* la frase diceva "valida, normalizza, e basta", cioè l'opposto della
> decisione presa poche righe sopra — dove si sceglie C (il server rifiuta) proprio contro
> B (il server normalizza). Una normalizzazione applicata in un punto e dimenticata in un
> altro è il modo esatto in cui si spacca una stanza in due.

## Conseguenze

**Più facile:** un ID resta dettabile a voce e scrivibile su un foglio, ma diventa anche
memorizzabile e pronunciabile quando si sceglie una parola (`RIUNIO`, `CANTIE`) — cosa
che con sole cifre era impossibile.

Soprattutto, lo spazio passa da 10⁶ a **36⁶ ≈ 2,18 miliardi**, circa 2.177 volte tanto.
L'enumerazione esaustiva, che con gli ID numerici richiedeva pochi minuti, richiederebbe
oltre 25 giorni di scansione ininterrotta a 1.000 tentativi al secondo. Sommata al limite
di 5 join per 3 minuti per IP, smette di essere una minaccia pratica.

Questo declassa il rate limiting da **unica difesa** a **seconda di due**, e cambia il
tono della sezione [07 §4](../07-sicurezza-e-privacy.md): da debolezza strutturale del
design a rischio residuo.

**Più difficile:** compare un problema che con le sole cifre non esisteva, i **caratteri
confondibili**. `0` e `O` sono quasi identici in molti font, e così `1` e `I`. Un ID
dettato al telefono può essere trascritto male, e l'errore produce di nuovo una stanza
nuova e vuota invece di un messaggio.

È stato deciso di **non intervenire**: nessun simbolo escluso dall'alfabeto, nessuna
sostituzione automatica in ingresso. Tutti i 36 caratteri restano digitabili e vengono
presi alla lettera. L'unica mitigazione è il **font monospace** nel campo dell'ID, che
separa visivamente `0` da `O` e `1` da `I`.

Il ragionamento dietro il "non intervenire": una sostituzione automatica `O` → `0`
renderebbe impossibile creare una stanza chiamata `OTTOBR`, e introdurrebbe una seconda
regola di trasformazione oltre alla conversione in maiuscolo — con il rischio, se
applicata in modo incoerente fra client e server, di reintrodurre proprio il fallimento
silenzioso che la forma canonica unica elimina. Meglio un alfabeto prevedibile e un font
che si legge.

E resta il punto vero: **un ID scelto da una persona non è casuale.** `TEST01`, `AAAAAA`,
`123456` sono trovabili con un dizionario di poche migliaia di tentativi, quale che sia
l'ampiezza dello spazio teorico. La difesa efficace non è allungare l'ID, è generarlo a
caso o proteggere la stanza con una password.

**Aggiornamento del 2026-09-07:** con l'introduzione della sala d'attesa
([ADR-0007](0007-accesso-su-approvazione.md)), indovinare un ID non fa più entrare in una
stanza — porta solo in una coda visibile a tutti. Il rischio degli ID prevedibili scende
di conseguenza da "chiunque indovini legge tutto" a "chiunque indovini può chiedere di
entrare". Resta utile un generatore di ID casuali, ma non è più una difesa critica.
