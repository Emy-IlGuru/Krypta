# ADR-0006: I partecipanti sono visibili dentro la stanza

**Data:** 2026-09-06
**Stato:** accettata — **modifica una posizione precedente**

## Contesto

La prima stesura dei documenti conteneva una garanzia: *"Non esiste una lista membri. Si
vede solo quanti sono, non chi sono. Chi resta in silenzio è invisibile."*

La specifica successiva la contraddice su due punti:

- ingressi e uscite producono un messaggio visibile: *"{nick} si è unito alla stanza"*;
- il comando `/Users` elenca i partecipanti, e l'elenco è mostrato a tutti.

Non è un dettaglio implementativo: cambia una delle promesse del prodotto. Va registrato
esplicitamente, altrimenti resta un'incoerenza silenziosa fra ciò che i documenti dicono
e ciò che il software fa.

## Opzioni valutate

**1. Mantenere l'anonimato interno.** Nessun annuncio di ingresso, nessun `/Users`, solo
il contatore. Massima riservatezza fra partecipanti, ma un'esperienza spiazzante: non
sapere chi c'è in una chat rende difficile persino capire se la persona che aspetti è
arrivata.

**2. Visibilità completa** — annunci di ingresso/uscita e `/Users` su richiesta.

**3. Compromesso**: annunci anonimi ("un partecipante è entrato") e nessun elenco. Dà il
senso della presenza senza i nomi, ma è un mezzo servizio: non risolve il bisogno pratico
e non mantiene l'anonimato, perché con poche persone si deduce comunque chi è entrato.

## Decisione

**Opzione 2: dentro la stanza si sa chi c'è.**

E, di conseguenza, **la promessa di privacy viene riformulata**. Krypta non protegge più
dagli altri partecipanti. Protegge dall'archivio:

- l'elenco è **costruito nell'istante** in cui `/Users` viene invocato, non è una
  struttura che il server mantiene aggiornata;
- non è memorizzato, non è esportabile, non compare nei log
  ([04 §6](../04-api-server.md));
- sparisce con la stanza, insieme a tutto il resto;
- ed è **simmetrico**: `/Users` è trasmesso a tutti i partecipanti, non solo a chi lo ha
  chiesto. Nessuno può verificare chi c'è di nascosto.

Il confine si sposta ma non scompare: **chi è dentro sa, chi è fuori non saprà mai**, e
nessuno potrà ricostruire a posteriori chi c'era.

## Conseguenze

**Più facile:** l'app diventa usabile per il suo caso d'uso reale. Coordinarsi con un
gruppo senza sapere chi è collegato è frustrante, e il contatore da solo non basta.
La disambiguazione dei nickname ([ADR-0003](0003-colori-nickname-deterministici.md))
diventa anche più utile: vedere `emy` ed `emy2` nell'elenco spiega subito cos'è successo.

**Più difficile:** un partecipante che voleva restare in ascolto senza farsi notare non
può più. Chi ha bisogno di non essere identificato deve scegliere un nickname che non lo
identifichi — e questo va detto nella UI, non solo nei documenti.

Cade anche una difesa indiretta: prima, chi entrava con un ID indovinato restava
invisibile agli altri **ma** non poteva sapere chi ci fosse. Ora un intruso apprende
subito l'elenco dei presenti. Il contrappeso è che il suo ingresso viene annunciato: se
compare un nome sconosciuto, tutti lo vedono.

**Documenti allineati:** [00 §4](../00-visione-e-obiettivi.md) (differenza 4),
[07 §2](../07-sicurezza-e-privacy.md) (garanzia G6 riscritta), [07 §3.2](../07-sicurezza-e-privacy.md)
(aggiunta l'esclusione esplicita dell'anonimato interno).

**Da rivedere se:** emerge un caso d'uso che richiede davvero l'ascolto anonimo. In quel
caso non va rimosso `/Users`, ma introdotta una modalità di stanza esplicita — perché
un elenco che a volte funziona e a volte no è peggio di entrambe le alternative.
