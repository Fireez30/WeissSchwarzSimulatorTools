# How WeissSchwarz Simulator Tools is built

A guided tour of the code. Read it top to bottom once; afterwards, the "Where is…?" table is the
quickest way in.

The tool was first written in C++ with Qt, then ported to Rust, then to C#. A few comments mention
"the original tool": they explain why some behaviour (such as the order card code spellings are
tried in) was kept exactly as it was.

## The big picture

The program reads the files of a Weiss Schwarz Simulator install (cards, decks, sleeves) and
shows them in a window. It can also build and edit decks, import and publish decks on
[encoredecks.com](https://www.encoredecks.com), print decks as PDFs and simulate the damage of
a turn.

The code is split in three projects (`WeissSchwarzSimulatorTools.slnx` ties them together):

| Project                  | Contains                                                                  |
|--------------------------|---------------------------------------------------------------------------|
| `WeissSchwarzSimulatorTools.Core`    | Everything that is not drawing: file formats, deck rules, network, PDFs, the damage simulator. No UI code. |
| `WeissSchwarzSimulatorTools.App`     | The window, built with [Avalonia](https://avaloniaui.net) (XAML views and their view models). |
| `WeissSchwarzSimulatorTools.Tests`   | The tests of the library (xUnit).                                         |
| `WeissSchwarzSimulatorTools.App.Tests` | Renders every screen to PNG files, in both themes (Avalonia headless).  |

The app calls the library; the library never calls the app. When you change *what the program
does* (a file format, a matching rule), it is almost always in Core; when you change *how it
looks or reacts*, it is in App.

Settings shared by every project (target framework, version) are in `Directory.Build.props`.

## Where is…?

```text
WeissSchwarzSimulatorTools.Core/
├── Model.cs                The data types: Card, CardSet, Serie, Deck, CardColor, CardType, Trigger.
├── Text.cs                 Decoding text files, the card text markup (<b>, <sprite…>), quoted names.
├── CardCodes.cs            The many spellings of a card code (KS/W49-001 vs KS/W49-E001…).
├── CardIndex.cs            Finding loaded cards by code (CardIndex) or by name (NameIndex).
├── Filters.cs              The card list's filters, and the two sort orders.
├── DeckBuilder.cs          A deck being built (DeckList) and the deck rules (50 cards, 4 copies…).
├── DamageSim.cs            The damage calculator: playing a turn of attacks many times.
├── Settings.cs             The settings file, and every key it holds.
├── Fonts.cs                The fonts compiled into the program.
├── Errors.cs               UserException: errors with a message meant for the user.
├── Http.cs                 The HTTP clients (one never follows redirects: see Encore Decks).
├── Simulator/              The simulator's install folder:
│   ├── SimulatorFolder.cs    where each kind of file lives; finding installs; loading every card
│   ├── SeriesFile.cs         SingleSetData.txt: the series and their sets
│   ├── CardFile.cs           CardData.txt: the cards of a set
│   ├── CommonEffects.cs      effect texts shared by many cards
│   ├── CardImages.cs         finding a card's picture
│   ├── DeckFiles.cs          reading and writing decks
│   └── Sleeves.cs            sleeve images, and adding new ones
├── EncoreDecks/            encoredecks.com:
│   ├── Decks.cs              reading decks, importing them into the simulator
│   ├── Upload.cs             publishing a deck
│   ├── AccountApi.cs         signing in, checking a session
│   └── Credentials.cs        keeping the session in the system's credential store
└── Pdf/                    Printable PDFs: Printouts.cs lays out proxies and translation sheets,
                            Document.cs (PDFsharp) and Artwork.cs (SkiaSharp) do the drawing.

WeissSchwarzSimulatorTools.App/
├── Program.cs              Opens the window; the embedded fonts.
├── App.axaml               The theme: Fluent, Material icons, our colours and components.
├── Theme/Colors.axaml      The Material 3 colour roles, light and dark.
├── Theme/Material.axaml    The components: filled/tonal/outlined/text buttons, chips, cards…
├── Controls/               Controls made in code: CardThumb (a card picture), CardViews (a card's
│                           summary and effect text), RelatedCards (window), Pictures (image cache).
├── ViewModels/             The state of each screen, and what its buttons do:
│   ├── MainViewModel.cs      the window: screens, loading the simulator, notices, dialogs, theme
│   ├── SetupViewModel.cs     first screen: picking the simulator folder
│   ├── CardBrowserViewModel.cs  the Cards screen (also used inside the deck editor)
│   ├── DetailPaneViewModel.cs   the pane showing one card (hovered or pinned)
│   ├── DecksViewModel.cs     the Decks screen
│   ├── DeckEditorViewModel.cs   building or editing a deck
│   ├── DeckPreviewViewModel.cs  an open deck's stats and card groups (Decks and Encore screens)
│   ├── DeckDialogs.cs        Import, Upload and PDF dialogs
│   ├── SleevePickerViewModel.cs the sleeve picker popup
│   ├── EncoreViewModel.cs    the Encore Deck screen: the linked account's decks
│   ├── DamageViewModel.cs    the Damage screen
│   └── AccountViewModel.cs   linking the Encore Decks account (Settings)
└── Views/                  The XAML of each screen and dialog, one file per view model.
```

## How the data flows

**Startup.** `MainViewModel`'s constructor reads the settings. If a simulator folder is saved,
it starts loading it; otherwise the setup screen asks for one, and looks for installs on the
computer in the background (`SimulatorFolder.DetectInstalls`).

**Loading the cards.** `SimulatorFolder.LoadSeries` reads every `SingleSetData.txt` and
`CardData.txt` of the install (series are parsed in parallel with PLINQ) and returns a
`List<Serie>`: every card, grouped by serie and set. `MainViewModel.Series` keeps it, and hands
it to every screen.

**Opening a deck.** A deck file only holds card codes (`DeckFile`). To show it, the codes are
matched to loaded cards with `CardIndex.ResolveDeck`, which tries the other spellings of each
code (`CardCodes`) and returns a `Deck`: its cards, plus the codes that matched nothing
(`Deck.Unmatched`). `DeckPreviewViewModel` then counts types, levels, colours and triggers for
the stat tiles.

**Importing from Encore Decks.** `Decks.ImportAsync` downloads the deck's JSON, matches its
codes the same way, and saves it with `DeckFiles.SaveNew`, which never overwrites a file (a
taken name gets a ` (2)` suffix).

**Uploading.** `Upload.UploadAsync` looks every card up on the site (it stores decks as its own
card ids), and publishes nothing unless all 50 cards were found.

**Simulating damage.** The Damage screen builds a `Turn` (the soul of the three characters on
stage, the sources of damage in order, the trigger cards left in the player's deck, and the
levels of the deck minus the stage and the climax for Musashi effects) and an `Opponent`, then
runs `Simulator.Simulate` in the background. The result is a histogram of the total damage, from
which the screen shows the average, the chance to kill and the chance of each total.

**Printing.** `Printouts.Proxies` and `Printouts.Translation` build the PDF bytes; the export
dialog writes them to the Downloads folder.

## How the window works

### Views and view models (MVVM)

Each screen is a pair: a **view model** (`ViewModels/`, plain C#) holds its state and its
actions, and a **view** (`Views/`, XAML) draws it. The view never changes the state itself: it
*binds* to the view model's properties and commands.

```xml
<TextBox Text="{Binding Search}" />
<Button Classes="filled" Command="{Binding SaveCommand}" Content="Save" />
```

The view models use [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/):

```csharp
[ObservableProperty]
private string _search = "";              // generates the Search property, which tells the view when it changes

partial void OnSearchChanged(string value) => Refilter();   // runs after each change

[RelayCommand]
private void Save() { … }                 // generates SaveCommand, for buttons
```

A property computed from others (`public bool CanSave => …`) is refreshed in the view with
`OnPropertyChanged(nameof(CanSave))`, or `[NotifyPropertyChangedFor(nameof(CanSave))]` on the
fields it depends on.

Bindings are compiled (`x:DataType` on each view): a typo in a property name is a build error.

### Slow work

Anything slow (the network, reading all the cards, writing a PDF) must not run on the UI thread,
or the window would freeze. It runs with `await Task.Run(…)` in an `async` method; the code
after the `await` is back on the UI thread, where it can update the view model:

```csharp
Running = true;                                   // the view shows a spinner
var deck = await Task.Run(() => Decks.ImportAsync(link, series, dir));
Running = false;                                  // back on the UI thread
```

### Telling the user

Screens do not show error windows themselves: they call `MainViewModel.Notify`. `Notice.Info`
becomes a snackbar at the bottom of the window; `Notice.Error` an error dialog. An error caused
by an expired Encore Decks session also unlinks the account. Library errors meant for the user
are `UserException`s: their message says what went wrong ("reading the deck: file not found").

Dialogs (Import, Upload, PDF, confirmations) are view models derived from `DialogViewModel`,
shown with `MainViewModel.ShowDialog`; `MainWindow.axaml` picks the view of each one by type.

### Pictures

Card pictures are read in the background and scaled down to the size they are shown at, by the
attached property `ctl:Pictures.Path` on an `Image` (`Controls/Pictures.cs`), which keeps the
last few hundred in a cache. The card grid is a virtualised list of rows: only the rows on
screen exist, so a serie of a thousand cards scrolls smoothly.

## Building, running, testing

```sh
dotnet run --project WeissSchwarzSimulatorTools.App                   # build and open the window
dotnet test                                               # every test that needs nothing more
WSST_LIVE_TESTS=1 dotnet test WeissSchwarzSimulatorTools.Tests --filter Category=Live   # encoredecks.com and the system keyring
SIMULATOR_ROOT=/path/to/simulator dotnet test WeissSchwarzSimulatorTools.Tests --filter Category=RealInstall
SIMULATOR_ROOT=/path/to/simulator SCREENSHOT_DIR=/some/dir dotnet test WeissSchwarzSimulatorTools.App.Tests
```

The real-install, live and screenshot tests are skipped when their variable is not set. The real-install tests only read the simulator folder. The screenshot test renders every
screen in both themes: run it before and after a change to compare.

On Linux distributions without ICU (some immutable ones), run the SDK with
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (the scripts do). The program itself never needs ICU:
it only uses the invariant culture (`InvariantGlobalization` in `Directory.Build.props`). It
needs no system fonts either: Noto Sans is embedded, and SkiaSharp is the build without
fontconfig.

## Common changes

- **A new setting:** add its key in `Settings.cs`, read it in `MainViewModel`'s constructor,
  write it with `MainViewModel.SaveSetting`.
- **A button on the open deck:** add a `[RelayCommand]` method in `DecksViewModel`, and the
  button in `Views/DecksView.axaml`.
- **A card code that does not match:** the spellings tried are in `CardCodes.cs` (`KeyVariants`
  for the simulator, `EncoreSpellings` for Encore Decks); add a case to their tests first.
- **A card file line that is not read:** `Simulator/CardFile.cs`, method `ParsePass`.
- **A colour or a size:** `Theme/`. Views use colour roles (`{DynamicResource Primary}`), never
  raw colours, so both themes follow.

## Weiss Schwarz vocabulary

- **Serie**: a franchise (KonoSuba, Hololive…). Spelled "serie" in the code, as the original
  tool did.
- **Set**: a product of a serie (a booster box, a trial deck), with a code prefix (`KS/W49`).
- **Card code**: `KS/W49-E001`. English cards add an `E` after the dash; foils add letters at
  the end (`-001SP`).
- **Character, Event, Climax**: the card types. A deck has exactly 50 cards, at most 8 climaxes,
  and at most 4 cards with the same name.
- **Level, cost, power, soul**: a card's stats. **Traits** are tags such as `<Magic>`.
- **Trigger**: the icon(s) on a card that act when it is revealed (soul, book, door…).
- **Climax combo (CX combo)**: an effect that needs a specific climax.
- **Sleeve**: the card-back image the simulator draws a deck with.
- **Encore Decks**: encoredecks.com, a site to share decks.
