# Changelog

Alle noemenswaardige wijzigingen in kniip staan in dit bestand. De nieuwste versie staat bovenaan.

## 2.3.4 - 2026-11-13

### Gewijzigd

- kniip zoekt nieuwe versies nu op GitHub ([github.com/R0GGER/kniip/releases](https://github.com/R0GGER/kniip/releases)) in plaats van op git.hibbit.net. Onder **Instellingen > Update** komen de changelog en de installer voortaan van GitHub.

## 2.3.2 - 2026-11-12

### Gewijzigd

- Bovenaan het systeemvakmenu staat nu altijd **Open kniip**. Zijn er tabbladen in de editor, dan toont het de editor; anders opent de afbeeldingsgeschiedenis. Dit vervangt **Editor tonen**, dat alleen verscheen als er tabbladen waren.
- De items in het systeemvakmenu hebben nu een icoon, in dezelfde stijl als de editor. De iconen volgen de lichte of donkere kleurmodus.

## 2.3.1 - 2026-11-12

### Gewijzigd

- De stip op het kniip-icoon in het systeemvak die een nieuwe versie meldt, is nu oranje in plaats van blauw. Zo valt hij beter op.

## 2.3.0 - 2026-11-11

### Nieuw

- **Gum.**\
  Haal delen van een knipsel weg; die plekken worden transparant. Je ziet daar het dambord of de achtergrond die je voor het canvas hebt gekozen, en een PNG blijft daar doorzichtig. Kies de gum met de knop in de werkbalk of met G. In de eigenschappenbalk kies je hoe hij werkt:
  - **Smart-gum** (standaard): klik op een kleur en alle aangrenzende pixels met dezelfde kleur verdwijnen, bijvoorbeeld een witte achtergrond rond een logo. Met **Tolerantie** bepaal je hoe ver een kleur mag afwijken; hoger neemt ook tinten mee die er net naast liggen.
  - **Vrije hand**: sleep over wat weg moet. Je kiest de grootte van de gum; een cirkel bij de muis laat zien hoe groot hij is.
  - Staan er aantekeningen op het knipsel, dan voegt de gum die eerst samen met de afbeelding. **Ongedaan maken** (Ctrl+Z) zet in één stap alles terug, ook de losse aantekeningen.
- **Tekst uitlijnen in een kader.**\
  Zet tekst links, gecentreerd of rechts met de drie knoppen naast **Vet** in de eigenschappenbalk. Dit werkt voor kader, ballon, ovaal en wolk, ook terwijl je typt. Het verschil zie je bij tekst over meerdere regels, of als je het kader breder hebt gesleept dan de tekst. De keuze geldt ook voor het volgende tekstkader.
- **Tekst zonder kader.**\
  Kies bij het tekstgereedschap de vorm **Geen kader** om losse tekst op de afbeelding te zetten, zonder rand of achtergrond. De instellingen voor rand, achtergrond, dikte, streep en hoeken verdwijnen dan uit de eigenschappenbalk. Met **Schaduw** krijgt de tekst zelf een schaduw, zodat hij ook op een drukke achtergrond leesbaar blijft.

### Gewijzigd

- Een tabblad sluiten (kruisje, middelste muisknop of Ctrl+W) vraagt niet meer of je de aantekeningen naar het klembord wilt kopiëren; het tabblad gaat meteen dicht. Bij het sluiten van alle tabbladen, afsluiten van kniip en een update installeren vraagt kniip het nog wel.

## 2.2.1 - 2026-11-11

### Nieuw

- **Update-melding in het systeemvak.**\
  Is er een nieuwere versie, dan krijgt het kniip-icoon in het systeemvak een blauwe stip rechtsonder. Wijs het icoon aan en de tooltip meldt welke versie er klaarstaat. De stip verdwijnt na het updaten, of als je **Automatisch controleren op nieuwe versies** uitzet in de tab **Update**.

### Gewijzigd

- De tabbladen staan nu in de titelbalk van de editor, net als in Chrome en Firefox. Dat scheelt een balk aan ruimte. Sleep het venster aan de lege ruimte naast de tabbladen; dubbelklik daar om te maximaliseren of terug te zetten. Zijn er veel tabbladen open, dan worden ze smaller in plaats van over twee rijen te lopen.
- Het kniip-icoon staat linksboven, voor de tabbladen. Klik erop voor het venstermenu (verplaatsen, formaat wijzigen, minimaliseren, maximaliseren, sluiten); dubbelklik sluit het venster.
- De standaardwerkbalk is nu **Drie-split** met zwevende knoppen voor screenshot, screencast en tekst herkennen, links verticaal in het midden van het canvas. Dit geldt voor nieuwe installaties, **Standaardinstellingen** in de instellingen en **Standaard herstellen** in het rechtsklikmenu van de werkbalk.

## 2.2.0 - 2026-11-11

### Nieuw

- **Tabbladen in de editor.**\
  Alle knipsels openen nu als tabblad in één editorvenster, in plaats van elk in een eigen venster. Wissel met een klik op het tabblad, met Ctrl+Tab en Ctrl+Shift+Tab, of met Ctrl+PageDown en Ctrl+PageUp. Sluit een tabblad met het kruisje, met de middelste muisknop of met Ctrl+W. Een sterretje achter de naam betekent dat de aantekeningen nog niet gekopieerd, opgeslagen of geüpload zijn.
  - Sluit je het venster, dan vraagt kniip of je alle tabbladen wilt sluiten of ze wilt behouden. Behouden verbergt het venster; de tabbladen komen terug bij het volgende knipsel, of via **Editor tonen** in het systeemvakmenu. Vink **Onthouden** aan om niet meer te vragen.
  - De keuze wijzig je in de instellingen onder **Weergave** > **Editorvenster sluiten**.
  - Afsluiten van kniip en een update installeren sluiten altijd alle tabbladen. Bij een tabblad met niet-opgeslagen aantekeningen vraagt kniip eerst of je het resultaat wilt kopiëren.

### Gewijzigd

- **Nieuw** (Ctrl+N), plakken (Ctrl+V), een afbeelding openen (Ctrl+O) en een afbeelding naar de editor slepen openen een nieuw tabblad. Het huidige knipsel blijft staan, dus kniip vraagt niet meer of je het wilt weggooien. Alleen een leeg canvas waarop je nog niets hebt gedaan, maakt plaats voor het nieuwe knipsel.
- **Wissen** vervangt het huidige tabblad nog steeds door een leeg canvas.

## 2.1.0 - 2026-11-11

### Nieuw

- **Printen.**\
  Druk een knipsel met je aantekeningen af. De knop met het printericoon staat links van het tandwiel, of druk op Ctrl+P. Je kiest de printer en de afdrukstand in het printvenster van Windows. Het knipsel komt in het midden van de pagina; is het te groot, dan wordt het verkleind.
- **Mappen openen.**\
  Links van de printknop staat een knop met een mapicoon. Klik erop en kies **Open Screenshot map** of **Open Screencast map**. Je krijgt dezelfde mappen als in het systeemvakmenu: de opslagmap uit de instellingen, anders de map waarin je het laatst hebt opgeslagen.
  - Staan de linialen aan, dan zit er ook een mapknop in het hoekje waar de linialen samenkomen. Die opent hetzelfde menu, met **Screenshotmap openen** en **Screencastmap openen**. Rechtsklik op het hoekje toont nog steeds het menu om hulplijnen te wissen of de linialen te verbergen.
  - Is er nog geen screencastmap, dan maakt kniip die aan (`Video's\kniip`). Daardoor kun je **Screencasts** in het systeemvakmenu nu ook kiezen voordat je je eerste screencast hebt opgeslagen.
- **Meer opslagopties onder de Opslaan-knop.**\
  Rechts in de Opslaan-knop staat een klein pijltje. Klik erop voor een menu met de andere manier van opslaan en **Opslaan als...**. Staat de standaardknop in de instellingen op **Opslaan en kopiëren**, dan kies je hier **Opslaan** zonder te kopiëren; staat hij op **Opslaan**, dan kies je hier **Opslaan en kopiëren**. **Opslaan als...** vraagt altijd waar het bestand moet komen. Het menu staat ook in het screencastvenster, met het gekozen formaat erbij, zoals **Opslaan als MP4**.

## 2.0.0 - 2026-11-10

### Nieuw

- **OCR (tekst herkennen).**\
  Lees tekst van het scherm en kopieer hem. Naast de knoppen voor een schermafdruk en een screencast staat nu de knop **OCR**, met hetzelfde icoon als in het Knipprogramma. Klik erop, sleep een rechthoek, en de tekst daarin opent in een apart venster. Daar pas je hem aan en kopieer je alles, of alleen wat je selecteert. De editor gaat tijdens het kiezen even opzij en komt daarna terug.
  - Druk in de editor op Ctrl+Shift+T om de tekst van het geopende knipsel te lezen, met je aantekeningen erbij.
  - Je vindt OCR ook in het menu van het icoon in het systeemvak, onder **OCR (tekst herkennen)...**. Geef het in de instellingen onder **Sneltoetsen** een eigen sneltoets.
  - kniip gebruikt de tekstherkenning van Windows, in de talen van je Windows-account. Vindt Windows geen taal voor tekstherkenning, dan staat in het venster hoe je er een toevoegt.
- **Verloop in elke richting.**\
  De richting van een verloop als achtergrond kies je nu met een draaiknop in plaats van uit vier vaste richtingen. Klik op de knop naast de kleuren en sleep de knop rond de cirkel. Je ziet het verloop meteen meedraaien.
  - Hij klikt vast op de acht hoofdrichtingen. Houd Shift ingedrukt voor stappen van 15°, of Alt om niet vast te klikken. Je kunt ook de hoek in graden typen, of scrollen boven de knop.
  - **Vanuit het midden** staat nu als knop onder de cirkel.
  - Elke nieuwe richting is één stap in **Ongedaan maken**, ook als je lang sleept.

### Gewijzigd

- Het blokjespatroon achter transparante delen is lichter en daardoor rustiger om naar te kijken.

## 1.1.9 - 2026-11-10

### Gewijzigd

- Klik je op het icoon in het systeemvak, dan maak je direct een schermafdruk: sleep een rechthoek om te knippen. Het menu opent nog steeds met de rechtermuisknop. Daarin staat **Rechthoek knippen** nu vetgedrukt als standaardactie, en **Bewerken vanaf klembord** staat er nog gewoon in.

## 1.1.8 - 2026-11-10

### Nieuw

- **Transparant canvas en achtergrond.**\
  Een leeg canvas is nu transparant in plaats van wit. In de editor zie je dat aan een blokjespatroon, en een opgeslagen PNG houdt de transparantie. Wil je toch een achtergrond, kies dan het selectiegereedschap zonder iets te selecteren: in de balk met eigenschappen staat **Achtergrond**. Kies **Geen (transparant)**, **Kleur**, **Verloop** (twee kleuren en een richting) of **Afbeelding** (vult het hele canvas).
  - De achtergrond komt alleen onder de transparante delen. Bij een gewoon schermknipsel staat de keuze er daarom niet. Bij een geopende PNG met transparantie wel.
  - Vervagen en pixeleren werken ook op de achtergrond. **Samenvoegen** laat de achtergrond apart, zodat je hem daarna nog kunt wijzigen.
  - Een JPEG kent geen transparantie. Daarin, en in de bitmap op het klembord voor programma's die geen PNG lezen, worden transparante delen wit.
  - Sommige programma's zetten een afbeelding op het klembord die volledig doorzichtig lijkt. Zo'n afbeelding opent kniip nu als gewone, ondoorzichtige afbeelding, zodat hij niet onzichtbaar wordt.
  - Elke wijziging van de achtergrond is één stap in **Ongedaan maken**.
- **Drie-split-weergave.**\
  Een derde indeling voor de editor: de gereedschappen staan boven, **Opslaan**, **Uploaden**, **Kopiëren** en de zoomknoppen onder, en **Nieuw** en **Screencast** apart. Kies de indeling in de instellingen onder **Weergave** > **Werkbalk**, of klik met rechts op een werkbalk en kies **Indeling**.
  - De balk met **Nieuw** en **Screencast** staat links, rechts, boven of onder het canvas, of **Zwevend** boven het canvas. Kies de plek in de instellingen of via rechtsklik > **Nieuw en Screencast**.
  - Zwevende knoppen versleep je aan het greepje of de rand naar elke plek boven het canvas. Bij de boven- of onderrand klikken ze vast en liggen ze horizontaal, verder liggen ze verticaal. De plek wordt onthouden. Klik met rechts en kies **Zwevende knoppen vastzetten** om ze niet per ongeluk te verslepen.
  - In de balk staan alleen de iconen. Ctrl+H verbergt ook deze balk, en **Standaard herstellen** zet hem weer links.
- **Linialen en hulplijnen.**\
  Boven en links van het canvas staan linialen met een schaalverdeling in pixels van de afbeelding. Ze zoomen en scrollen mee, en een blauw streepje laat zien waar de muis staat. Zet ze aan of uit met de knop **Linialen** naast de zoomknoppen, of met Ctrl+R.
  - Sleep vanuit een liniaal naar het canvas voor een hulplijn: de bovenste liniaal geeft een horizontale lijn, de linker een verticale. Met het selectiegereedschap versleep je een hulplijn. Sleep hem terug naar de liniaal of buiten de afbeelding om hem te verwijderen. Klik met rechts op het hoekje tussen de linialen of op het canvas en kies **Hulplijnen wissen** om ze allemaal weg te halen.
  - Objecten klikken vast aan hulplijnen en aan de randen en het midden van de afbeelding: bij verplaatsen met de linker-, midden- of rechterkant (en boven, midden of onder), bij tekenen, bij het slepen van grepen en bij bijsnijden. De lijn waar het object aan vastklikt, licht roze op. Houd Alt ingedrukt om niet vast te klikken.
  - Hulplijnen zijn alleen een hulpmiddel in de editor. Ze komen niet in het opgeslagen of gekopieerde knipsel.
- **Uitlijnen, spiegelen en volgorde.**\
  Zodra je een object selecteert, staat in de balk met eigenschappen de knop **Uitlijnen**. Klik erop en er klappen knoppen uit om het object links, in het midden of rechts, en boven, in het midden of onder op de afbeelding te zetten. Ook kun je het horizontaal of verticaal spiegelen, en naar voren of naar achteren brengen. Klik nog eens op **Uitlijnen** om de knoppen weer in te klappen. Ze klappen ook in als je niets meer selecteert. Dezelfde opties staan in het rechtsklikmenu van een object, onder **Uitlijnen op de afbeelding**. Daar staat ook **Midden van de afbeelding**.
  - Spiegelen werkt voor lijnen, pijlen, markeringen uit de vrije hand, cursors, emoji en vormen. Bij rechthoeken, ellipsen, tekst, stappen en vervagen is de knop uitgeschakeld.
  - Elke klik is één stap in **Ongedaan maken**.
- **Vormen.**\
  Na **Ellips** staat een nieuwe knop **Vormen** (sneltoets F). Kies in de balk met eigenschappen welke vorm je tekent: driehoek, pijl, ster, wolk, ballon, hart, maan, druppel, ei, bliksemschicht of spiraal. Het icoon op de knop laat de gekozen vorm zien.
  - Sleep een kader om de vorm te tekenen. Met Shift worden breedte en hoogte gelijk. Met de grepen pas je het formaat later aan.
  - Je kiest randkleur, vulkleur, dikte, stippellijn en schaduw, net als bij een rechthoek. De spiraal is een open lijn en heeft geen vulling.
  - Selecteer een vorm en kies een andere in de keuzelijst om hem te vervangen. Spiegel of draai een pijl, maan of bliksem om hem een andere kant op te laten wijzen.
- **Draaien van vormen en emoji.**\
  Boven een geselecteerde vorm of emoji staat een ronde greep. Sleep die rond om het object te draaien. Het draait om zijn midden en klikt vast op 0°, 90°, 180° en 270°. Houd Shift ingedrukt voor stappen van 15°, of Alt om niet vast te klikken. Tijdens het draaien staat de hoek onderin de statusbalk.
  - Onder **Uitlijnen** staan knoppen om 90° linksom of rechtsom te draaien. Die opties staan ook in het rechtsklikmenu, samen met **Draaiing herstellen**. Dubbelklik op de ronde greep om de draaiing ook terug te zetten.
  - Een gedraaide vorm maak je met de grepen gewoon groter of kleiner. De schaduw valt altijd rechtsonder.
  - Elke draaiing is één stap in **Ongedaan maken**.
- **Tekstballon en -wolk.**\
  Bij **Tekst in kader** kies je in de balk met eigenschappen de vorm: **Kader**, **Ballon**, **Ovale ballon** of **Wolk**. Ballon en wolk hebben een staart waarmee je iets aanwijst.
  - Sleep de punt van de staart naar wat je wilt aanwijzen. Verplaats je het hele object, dan schuift de punt mee.
  - Bij een wolk bestaat de staart uit steeds kleinere bolletjes. Kies het aantal bolletjes, of laat het op **Auto** staan.
  - Een geselecteerd tekstobject heeft formaatgrepen. Maak het kader breder of smaller, dan loopt de tekst door naar de volgende regel. Dubbelklik op een greep om het kader weer automatisch op de tekst te laten passen.

## 1.1.7 - 2026-11-09

### Gewijzigd

- Het icoon in het systeemvak is groter en sluit aan bij de andere systeemvakpictogrammen.
- De tekst bij het icoon is nu **kniip**. Tijdens een opname staat daar **kniip - opname loopt**.

## 1.1.6 - 2026-11-09

### Nieuw

- **Donkere modus.**\
  Kies in de instellingen onder **Weergave** > **Kleurmodus** tussen **Licht**, **Donker** en **Systeem**. Bij **Systeem** volgt kniip de app-modus van Windows (Instellingen > Persoonlijke instellingen > Kleuren) en wisselt mee zodra je die verandert.
  - De kleurmodus geldt voor alle vensters: de editor, de instellingen, de geschiedenis, de screencastvensters, de meldingen en het systeemvakmenu. Ook de titelbalk wordt donker.
  - Na **Opslaan** wisselen open vensters meteen mee. Je knipsel zelf verandert niet van kleur.
  - **Licht** is de standaard, dus na de update ziet kniip er hetzelfde uit tot je iets anders kiest.

### Gewijzigd

- De tab **Editor** in de instellingen heet nu **Weergave**. Daar staan nu de kleurmodus, de markeerstift en de werkbalkindeling.
- De tab **Windows** heet nu **Systeem** en staat onder **Weergave**. De tabs **Knipsels** en **Nieuw knipsel** zijn opgegaan in de tab **Systeem**. Bovenaan staat de sectie **Knipprogramma** (klembord en map Schermafbeeldingen bewaken), daaronder **Bij een nieuw knipsel** en daarna **Windows-integratie**. De instellingen openen nu op de tab **Sneltoetsen**.
- Vinkjes en keuzerondjes in de instellingen hebben een nieuwe, rustigere stijl in de kleur van kniip.

### Opgelost

- De tekst in een tab van de instellingen was helemaal blauw en vet, net als de naam van de tab. Nu is alleen de tabnaam zo opgemaakt. De changelog in de tab **Update** volgt weer de markdown: gewone tekst normaal, koppen en **vet** alleen waar dat in de tekst staat.

## 1.1.5 - 2026-11-09

### Nieuw

- **Split-weergave.**\
  De editor kan nu ook twee werkbalken tonen. Bovenaan staan de gereedschappen in het midden, met **Instellingen** rechts en daaronder de eigenschappen (kleur, dikte, lijnstijl). Onderaan staan **Nieuw** en **Screencast** links, **Opslaan**, **Uploaden** en **Kopiëren** in het midden en de zoomknoppen rechts. Kies de indeling in de instellingen onder **Editor** > **Werkbalk**, of klik met rechts op een werkbalk en kies **Indeling**.
  - In beide werkbalken kun je knoppen verslepen (na ontgrendelen) en verbergen, net als bij één werkbalk. Beide indelingen onthouden hun eigen volgorde.
  - Split-weergave is de nieuwe standaard. Wil je de vertrouwde indeling terug, kies dan **Eén werkbalk**.
- **Vergrootglas bij het kiezen van een gebied.**\
  Bij het knippen van een rechthoek en bij het kiezen van een opnamegebied voor een screencast loopt naast de muisaanwijzer een rond vergrootglas mee. Je ziet de pixels rond de aanwijzer vergroot met een raster en een blauwe kruisdraad, en daaronder de schermcoördinaten (X en Y). Zo zet je een hoek precies op de goede pixel. Bij **Venster knippen** verschijnt het vergrootglas niet.
  - Het vergrootglas staat standaard aan. Wil je het niet, zet dan in de instellingen onder **Sneltoetsen** > **Gebied kiezen** het vinkje **Vergrootglas tonen bij het kiezen van een gebied** uit.
- **Schermafdruk vertraging.**\
  In het systeemvakmenu staat **Schermafdruk vertraging: 0s**. Stel daar met de schuifbalk of het getalveld 0 tot 60 seconden in. **Rechthoek knippen** en **Venster knippen** wachten dan zo lang voordat het scherm bevriest, ook via de sneltoets. Zo kun je eerst een menu openklappen of een tooltip laten verschijnen.
  - Bij de muis telt een klein venster af. Dat venster komt zelf niet in het knipsel. Druk op Esc of klik op het venster om te annuleren.
  - Bij 0 seconden knip je direct, zoals voorheen. Screencasts en de knop **Nieuw** in de editor wachten niet.
- **Geschiedenis.**\
  kniip houdt nu bij wat je opslaat en uploadt: knipsels en screencasts, met het bestand en de link. Knipsels die je alleen kopieert of niet bewaart, komen er niet in. Het systeemvakmenu heeft vier nieuwe items:
  - **Map openen**, met de map Schermafbeeldingen, je knipselmap en je screencastmap.
  - **Geschiedenis...** opent een lijst met datum, soort, naam, map en link. Je kunt zoeken op naam of link.
  - **Afbeeldingsgeschiedenis...** opent hetzelfde venster met miniaturen. Ook een knipsel dat je alleen hebt geüpload heeft een miniatuur.
  - **Recente items** toont de laatste tien. Klik op een item om het te openen, of ga naar het submenu voor **Openen in de editor**, **Link kopiëren**, **Afbeelding kopiëren**, **Pad kopiëren** en **Tonen in map**.

  Dubbelklik in het geschiedenisvenster om een item te openen. Klik met rechts voor dezelfde acties en voor **Uit geschiedenis verwijderen**. Met **Geschiedenis wissen** maak je de lijst leeg. Je bestanden en uploads blijven dan gewoon bestaan. kniip bewaart de laatste 500 items.

## 1.1.4 - 2026-11-09

### Nieuw

- **Melding bij een nieuwe versie.**\
  kniip kijkt bij het starten en daarna elke dag of er een nieuwere versie is. Is die er, dan staat er een blauwe stip op de knop **Instellingen** in de editor en op de tab **Update** in de instellingen. Wil je dat niet, zet dan in de tab **Update** het vinkje **Automatisch controleren op nieuwe versies** uit.
- **Uitgebreid rechtermuisknopmenu in de editor.**\
  Klik met rechts op het canvas, ook op een leeg stuk, voor een menu met iconen en sneltoetsen. Wat op dat moment niet kan, staat grijs.
  - **Ongedaan maken** en **Opnieuw**.
  - **Verwijder**, **Verwijder alles** (Shift+Delete, de afbeelding blijft staan) en **Samenvoegen** (Ctrl+Shift+F). Samenvoegen maakt de annotaties deel van de afbeelding en kun je ongedaan maken.
  - **Knip** (Ctrl+X), **Kopieer** (Ctrl+Shift+C), **Plak** (Ctrl+V) en **Dupliceren** (Ctrl+D). Plak via het menu zet de annotatie op de plek waar je rechts klikte. Kopiëren en plakken werkt ook tussen twee editorvensters.
  - **Naar voorgrond** (Home), **Naar voren** (PageUp), **Naar achteren** (PageDown) en **Naar achtergrond** (End).
  - **Werkbalken verbergen** (Ctrl+H). Via hetzelfde menu of Ctrl+H haal je ze terug. Bij een nieuwe start zijn ze er altijd weer.
- **Lijnen en pijlen buigen.**\
  Selecteer een lijn of pijl. Midden op elk stuk staat dan een klein rondje. Sleep dat rondje en het wordt een buigpunt. De lijn loopt daarna als vloeiende kromme door alle punten. Zo kun je zoveel buigpunten toevoegen als je wilt.
  - Dubbelklik op een buigpunt om het weer te verwijderen.
  - Bij een pijl draait de punt mee met de richting van het laatste stuk.
  - Met Shift springen begin- en eindpunt in stappen van 45°, gemeten vanaf het buigpunt ernaast.
  - Toevoegen, verschuiven en verwijderen van buigpunten kun je ongedaan maken.

### Gewijzigd

- kniip zoekt nu standaard zelf op de achtergrond naar updates. Tot nu toe gebeurde dat alleen als je de tab **Update** opende of op **Opnieuw controleren** klikte. Een mislukte controle, bijvoorbeeld zonder internet, geeft geen foutmelding.
- **Ctrl+V** plakt nu eerst een gekopieerde annotatie. Heb je daarna iets anders gekopieerd, dan opent Ctrl+V zoals voorheen de afbeelding van het klembord.
- Het rechtermuisknopmenu van de werkbalk heeft dezelfde nieuwe stijl gekregen.
- **Testversie naast kniip.**\
  `build.ps1` maakt nu ook `KniipTest-<versie>.exe`. Die testversie heeft eigen instellingen en registervermeldingen, zodat hij naast de geïnstalleerde kniip kan draaien zonder die te raken. De testversie werkt zichzelf niet bij. Met `-SkipTest` sla je hem over.

## 1.1.3 - 2026-11-09

### Nieuw

- **Sneltoets voor het klembord.**\
  In de tab **Sneltoetsen** kun je een combinatie zetten op **Vanaf klembord**. Die opent de afbeelding op het klembord in de editor. Standaard is er geen sneltoets. De gekozen combinatie staat ook bij **Bewerken vanaf klembord** in het systeemvakmenu.
- **Afbeelding naar de editor slepen.**\
  Sleep een afbeeldingsbestand, of een afbeelding uit een ander programma, naar het kniip-venster. kniip vraagt daarna of je die afbeelding wilt openen, zodat een lopende bewerking niet zomaar verdwijnt.

## 1.1.2 - 2026-11-09

### Opgelost

- **Link kopiëren na uploaden.**\
  Na het uploaden kon kniip de link soms niet op het klembord zetten en meldde dan dat het klembord bezet was. Dat gebeurde vooral als de klembordgeschiedenis van Windows (Win+V) of een ander klembordprogramma actief is. kniip zet de link nu op een directere manier op het klembord en probeert het langer opnieuw. Dit geldt ook voor het uploaden van screencasts en de test-upload in de instellingen.

## 1.1.1 - 2026-11-09

### Gewijzigd

- **Instellingen zonder scrollbalk.**\
  Het instellingenvenster is iets hoger geworden, zodat elke tab, ook **Opslaan** met de nieuwe naamvelden, in één keer past. De changelog in de tab **Update** heeft daardoor ook meer ruimte.

### Opgelost

- De downloadlinks in de handleiding (README) wezen naar een verkeerde tag en gaven een foutmelding.

## 1.1.0 - 2026-11-09

### Nieuw

- **Updaten vanuit kniip.**\
  In de instellingen staat een nieuwe tab **Update**. kniip kijkt op git.hibbit.net of er een nieuwere versie is en toont de wijzigingen van alle nieuwere versies, netjes opgemaakt. Met **Updaten** downloadt kniip de installer, sluit zichzelf af, installeert de nieuwe versie op de achtergrond en start daarna opnieuw. Je eerder gekozen instellingen, zoals starten met Windows en 'Openen met', blijven staan.
  - Na een update meldt kniip in het systeemvak naar welke versie het is bijgewerkt.
  - Er wordt alleen gecontroleerd als je de tab opent of op **Opnieuw controleren** klikt; kniip zoekt niet zelf op de achtergrond naar updates.
  - Loopt er nog een screencast, of wil je een open knipsel eerst opslaan, dan wacht de update tot dat klaar is.
- **Naamsjabloon voor opslaan.**\
  In de tab **Opslaan** stel je per soort (knipsels en screencasts) zelf in hoe bestanden heten. Je kunt deze codes gebruiken:
  - `%J` jaar, `%M` maand en `%D` dag (bijvoorbeeld `2026`, `11`, `09`)
  - `%V` de naam van het programma waarvan je een knipsel of opname maakt, bijvoorbeeld `chrome`
  - `%R6` zes willekeurige letters en cijfers
  - `%N3` een oplopend nummer met drie cijfers (`001`, `002`, ...), per map opnieuw geteld
  - `/` maakt een submap; kniip maakt die mappen zelf aan.

  Zo wordt `%J/%M-%D/%V_%N3` bijvoorbeeld `2026/11-09/chrome_001.png`. Onder het veld zie je meteen een voorbeeld. De extensie (`.png`, `.mp4` of `.gif`) zet kniip er zelf achter. Laat je het veld leeg, dan blijft de vertrouwde naam staan: `Knipsel` of `Screencast` met datum en tijd.
- **Autostart-vermelding opruimen.**\
  Heb je kniip ooit als losse exe gestart, dan kan er een vermelding in **Instellingen > Apps > Opstarten** achterblijven. Met `tools\remove-autostart.cmd` haal je die weg. Beheerdersrechten zijn daarvoor niet nodig.

### Gewijzigd

- Het versienummer staat nu ook in de naam van de losse exe (`Kniip-1.1.0.exe`), naast de installer (`Kniip-Setup-1.1.0.exe`).
- De handleiding (README) beschrijft nu het downloaden, installeren, verwijderen en publiceren van nieuwe versies, en heeft een schermafbeelding van de editor.

### Let op

- Vanaf 1.0.0 moet je deze versie nog één keer zelf installeren met `Kniip-Setup-1.1.0.exe`. Daarna kun je volgende versies bijwerken via **Instellingen > Update**.

## 1.0.0 - 2026-11-07

Eerste stabiele versie.

- **Annotatie-editor** naast het Windows Knipprogramma: voeg pijlen, lijnen, rechthoeken en andere vormen, tekst, markeringen, genummerde stappen, emoji, muisaanwijzers en vervaging toe aan een knipsel. Met ongedaan maken en opnieuw doen, en een werkbalk die je zelf kunt indelen.
- **Knipsels oppikken**: kniip ziet nieuwe afbeeldingen van het Knipprogramma op het klembord of in de map Schermafbeeldingen, en toont dan een melding of opent meteen de editor.
- **Eigen sneltoetsen** voor een rechthoek of een venster knippen, ook met Print Screen.
- **Screencasts** van een gekozen gebied, met systeemgeluid, microfoon, muisaanwijzer en gemarkeerde muisklikken. Daarna knippen en exporteren als MP4-video of GIF.
- **Opslaan, kopiëren en uploaden**: opslaan in een vaste map of elke keer kiezen, desgewenst ook naar het klembord, en uploaden naar een eigen Zipline-server. Na het uploaden staat de link op het klembord.
- **Windows-integratie**: draait op de achtergrond in het systeemvak, kan starten met Windows en staat in 'Openen met' voor afbeeldingen.
- **Installer** per gebruiker, zonder beheerdersrechten en zonder losse .NET-installatie.
