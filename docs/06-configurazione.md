# 06 — Configurazione del client

**Stato:** bozza · **Aggiornato:** 2026-09-06

## 1. Il file

| | |
|---|---|
| Nome | `krypta.config.xml` |
| Posizione | Nella stessa cartella dell'eseguibile del client |
| Codifica | UTF-8 |
| Scopo | Precompilare i campi delle schermate [1] e [2], così l'utente non ridigita host e porta a ogni avvio |

Il file è **facoltativo**. Se manca o è illeggibile, il client parte con i valori di
default e lo segnala nell'area di stato, senza bloccarsi.

## 2. Struttura

```xml
<?xml version="1.0" encoding="utf-8"?>
<KryptaConfig version="1">

  <!-- Server a cui il client si collega di default.
       Modificabile dall'utente nella schermata di connessione. -->
  <Server>
    <Host>127.0.0.1</Host>
    <Port>5000</Port>
    <UseTls>true</UseTls>
  </Server>

  <!-- Impronte TLS memorizzate al primo collegamento (TOFU).
       Se cambiano, il client rifiuta la connessione e avvisa. -->
  <KnownServers>
    <Server host="192.168.1.42" port="5000"
            sha256="9f:2a:c1:...:8e" firstSeen="2026-09-07" />
  </KnownServers>

  <!-- Identita' locale. Nessuna password: il nickname vale
       solo per la sessione in corso. -->
  <User>
    <Nickname>ospite</Nickname>
  </User>

  <!-- Preferenze dell'interfaccia. -->
  <Ui>
    <Theme>dark</Theme>              <!-- dark | light -->
    <FontSize>14</FontSize>
    <RememberSettings>true</RememberSettings>
  </Ui>

</KryptaConfig>
```

## 3. Campi

| Percorso | Tipo | Default | Vincoli | Se non valido |
|---|---|---|---|---|
| `Server/Host` | string | `127.0.0.1` | IPv4, IPv6 o hostname | Usa il default |
| `Server/Port` | int | `5000` | 1–65535 | Usa il default |
| `Server/UseTls` | bool | `true` | `true` \| `false` | Usa `true`. `false` è ammesso **solo** verso `127.0.0.1`, con avviso permanente nella UI |
| `KnownServers/Server` | elemento | *(vuoto)* | `host`, `port`, `sha256`, `firstSeen` | Voce ignorata se malformata: si ricade sul primo collegamento |
| `User/Nickname` | string | *(vuoto)* | 3–25 caratteri, esclusi i nomi riservati | Campo lasciato vuoto |
| `Ui/Theme` | enum | `dark` | `dark` \| `light` | Usa `dark` |
| `Ui/FontSize` | int | `14` | 10–24 | Usa `14` |
| `Ui/RememberSettings` | bool | `true` | `true` \| `false` | Usa `true` |

L'attributo `version` sull'elemento radice serve a gestire future migrazioni del formato.
Nell'MVP vale sempre `1`; se il client trova una versione più alta, avvisa e usa i default.

## 4. Precedenza

```
valori di fabbrica  <  krypta.config.xml  <  ciò che l'utente digita nella UI
```

Quello che l'utente scrive nei campi vince **sempre** su quello che c'è nel file. Il file
precompila, non impone.

## 5. Riscrittura del file

Il client riscrive `krypta.config.xml` **solo** quando:

- `Ui/RememberSettings` è `true`, **e**
- l'utente ha spuntato "Ricorda queste impostazioni" nella schermata [1], **e**
- la connessione è andata a buon fine.

In quel caso vengono aggiornati **solo** `Server/Host`, `Server/Port` e `User/Nickname`.

Il nickname salvato è **quello digitato dall'utente**, non quello eventualmente
disambiguato dal server: non ha senso ritrovarsi `emy2` precompilato al prossimo avvio,
quando magari il nome è tornato libero.

**Eccezione: le impronte TLS.** `KnownServers` viene scritto **anche senza** "Ricorda
impostazioni", perché è una misura di sicurezza e non una comodità: senza memorizzare
l'impronta, il TOFU non rileverebbe mai un certificato sostituito. Registra però a quali
server ci si è collegati — un dato che prima non finiva su disco. Va detto all'utente, e
il file deve poter essere ripulito da una voce di menu.

**Non viene mai scritto nel file:**

- ❌ chiavi private, chiave di stanza, o qualunque materiale crittografico dei messaggi
- ❌ gli ID delle stanze visitate
- ❌ i nomi assegnati alle stanze
- ❌ i nickname degli altri partecipanti, né gli elenchi restituiti da `/Users`
- ❌ qualsiasi contenuto di messaggio
- ❌ timestamp di sessione o cronologia degli accessi

Questa lista è vincolante: il file di configurazione non deve permettere a chi ha accesso
al PC di ricostruire dove l'utente è stato o con chi ha parlato.

## 6. Robustezza

| Situazione | Comportamento |
|---|---|
| File assente | Default silenziosi, nessun errore. Il file viene creato al primo salvataggio. |
| XML malformato | Default + avviso non bloccante: "Configurazione non leggibile, uso i valori predefiniti." Il file **non** viene sovrascritto automaticamente. |
| Elemento mancante | Default per quel solo campo, gli altri restano validi. |
| Valore fuori range | Default per quel solo campo. |
| File in sola lettura | Il salvataggio fallisce con avviso non bloccante. La sessione continua. |

Il principio è che un file di configurazione rotto non deve mai impedire di usare l'app.

## 7. Esempio: configurazione precompilata per un gruppo

Il caso d'uso tipico: chi ospita il server distribuisce ai partecipanti il client con un
config già pronto, e loro devono solo scegliere il nickname e digitare l'ID.

```xml
<?xml version="1.0" encoding="utf-8"?>
<KryptaConfig version="1">
  <Server>
    <Host>192.168.1.42</Host>
    <Port>5000</Port>
  </Server>
  <User>
    <Nickname></Nickname>
  </User>
  <Ui>
    <Theme>dark</Theme>
    <FontSize>14</FontSize>
    <RememberSettings>false</RememberSettings>
  </Ui>
</KryptaConfig>
```

Con `RememberSettings` a `false` il client non riscrive mai il file: utile su postazioni
condivise, dove non si vuole che il nickname dell'ultimo utente resti sul disco.
