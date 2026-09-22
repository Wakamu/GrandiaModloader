namespace Grandia.Sdk;

/// <summary>
/// Vanilla FIELD map stems (MDP hex id). Same numbers
/// <see cref="MapId"/> / <see cref="MapLoadEvent.To"/> / <see cref="Game.WarpTo"/>
/// use. Disc 2 reuses a few early ids (<see cref="OutsideSeaDragon"/> …);
/// those members keep one name and note both discs.
/// </summary>
public enum Maps : ushort
{
    /// <summary>Parm (intro) (<c>2000</c>).</summary>
    Parm = 0x2000,

    /// <summary>Parm (outside shed) (<c>2001</c>).</summary>
    ParmOutsideShed = 0x2001,

    /// <summary>Parm (night) (<c>2002</c>).</summary>
    ParmNight = 0x2002,

    /// <summary>Parm (early morning) (<c>2003</c>).</summary>
    ParmEarlyMorning = 0x2003,

    /// <summary>Seagull Restaurant (<c>2004</c>).</summary>
    SeagullRestaurant = 0x2004,

    /// <summary>Seagull living room (<c>2008</c>).</summary>
    SeagullLivingRoom = 0x2008,

    /// <summary>Justin's bedroom (<c>200C</c>).</summary>
    JustinsBedroom = 0x200C,

    /// <summary>Sue's house (<c>2010</c>).</summary>
    SuesHouse = 0x2010,

    /// <summary>Sue's room (<c>2012</c>).</summary>
    SuesRoom = 0x2012,

    /// <summary>Train gate (<c>2014</c>).</summary>
    TrainGate = 0x2014,

    /// <summary>Train platform (<c>2018</c>).</summary>
    TrainPlatform = 0x2018,

    /// <summary>Underground cafe (<c>201C</c>).</summary>
    UndergroundCafe = 0x201C,

    /// <summary>Baal Museum (<c>2020</c>).</summary>
    BaalMuseum = 0x2020,

    /// <summary>Curator's office (<c>2024</c>).</summary>
    CuratorsOffice = 0x2024,

    /// <summary>Baal Museum exhibit hall (<c>2028</c>).</summary>
    BaalMuseumExhibitHall = 0x2028,

    /// <summary>Gantz's house (<c>202C</c>).</summary>
    GantzsHouse = 0x202C,

    /// <summary>Blue Marlin (<c>2030</c>).</summary>
    BlueMarlin = 0x2030,

    /// <summary>Parm inventor's house (<c>2034</c>).</summary>
    ParmInventorsHouse = 0x2034,

    /// <summary>Parm house 1, first floor (<c>2038</c>).</summary>
    ParmHouse1FirstFloor = 0x2038,

    /// <summary>Parm house 1, second floor (<c>203A</c>).</summary>
    ParmHouse1SecondFloor = 0x203A,

    /// <summary>Parm house 2, first floor (<c>203C</c>).</summary>
    ParmHouse2FirstFloor = 0x203C,

    /// <summary>Parm house 2, second floor (<c>203E</c>).</summary>
    ParmHouse2SecondFloor = 0x203E,

    /// <summary>Parm house 3 (<c>2040</c>).</summary>
    ParmHouse3 = 0x2040,

    /// <summary>Parm house 4 (<c>2044</c>).</summary>
    ParmHouse4 = 0x2044,

    /// <summary>Parm house 5 (<c>2048</c>).</summary>
    ParmHouse5 = 0x2048,

    /// <summary>Parm general store (<c>204C</c>).</summary>
    ParmGeneralStore = 0x204C,

    /// <summary>Outside Sult (<c>2400</c>).</summary>
    OutsideSult = 0x2400,

    /// <summary>Sult Ruins 1 (<c>2404</c>).</summary>
    SultRuins1 = 0x2404,

    /// <summary>Sult Ruins 2 (<c>2406</c>).</summary>
    SultRuins2 = 0x2406,

    /// <summary>Sult Ruins (ancient passageway) (<c>2408</c>).</summary>
    SultRuinsAncientPassageway = 0x2408,

    /// <summary>Sult Ruins (Room of Illusion) (<c>240C</c>).</summary>
    SultRuinsRoomOfIllusion = 0x240C,

    /// <summary>Marna Road (<c>2410</c>).</summary>
    MarnaRoad = 0x2410,

    /// <summary>Outside Leck Mines (<c>2800</c>).</summary>
    OutsideLeckMines = 0x2800,

    /// <summary>Java's house (<c>2804</c>).</summary>
    JavasHouse = 0x2804,

    /// <summary>Leck Mines 1 (<c>2808</c>).</summary>
    LeckMines1 = 0x2808,

    /// <summary>Leck Mines 2 (<c>280C</c>).</summary>
    LeckMines2 = 0x280C,

    /// <summary>Leck Mines (minecart cutscene) (<c>2810</c>).</summary>
    LeckMinesMinecart = 0x2810,

    /// <summary>Leck Mines (deepest depths) (<c>2814</c>).</summary>
    LeckMinesDeepestDepths = 0x2814,

    /// <summary>Port of Parm (<c>2C00</c>).</summary>
    PortOfParm = 0x2C00,

    /// <summary>Steamer leaving the dock, cutscene 1 (<c>2C01</c>).</summary>
    SteamerLeavingDock1 = 0x2C01,

    /// <summary>Steamer leaving the dock, cutscene 2 (<c>2C02</c>).</summary>
    SteamerLeavingDock2 = 0x2C02,

    /// <summary>Port of Parm (night) (<c>2C04</c>).</summary>
    PortOfParmNight = 0x2C04,

    /// <summary>Port of Parm (early morning, steamer dock) (<c>2C05</c>).</summary>
    PortOfParmEarlyMorning = 0x2C05,

    /// <summary>Steamer (Feena swabbed decks) (<c>3000</c>).</summary>
    SteamerFeenaSwabbedDecks = 0x3000,

    /// <summary>Steamer deck (misty) (<c>3002</c>).</summary>
    SteamerDeckMisty = 0x3002,

    /// <summary>Steamer belowdecks (outside first-class hallway) (<c>3004</c>).</summary>
    SteamerBelowdecksFirstClass = 0x3004,

    /// <summary>Steamer belowdecks (outside second-class room) (<c>3008</c>).</summary>
    SteamerBelowdecksSecondClass = 0x3008,

    /// <summary>Steamer first-class hallway (<c>3010</c>).</summary>
    SteamerFirstClassHallway = 0x3010,

    /// <summary>Steamer crew quarters (<c>3014</c>).</summary>
    SteamerCrewQuarters = 0x3014,

    /// <summary>Steamer second-class cabin (<c>301C</c>).</summary>
    SteamerSecondClassCabin = 0x301C,

    /// <summary>Steamer first-class cabin A (<c>3020</c>).</summary>
    SteamerFirstClassCabinA = 0x3020,

    /// <summary>Steamer first-class cabin B (<c>3024</c>).</summary>
    SteamerFirstClassCabinB = 0x3024,

    /// <summary>Steamer lounge (<c>3028</c>).</summary>
    SteamerLounge = 0x3028,

    /// <summary>Steamer engine room (<c>302C</c>).</summary>
    SteamerEngineRoom = 0x302C,

    /// <summary>Steamer bridge (<c>3030</c>).</summary>
    SteamerBridge = 0x3030,

    /// <summary>Steamer deck (<c>3034</c>).</summary>
    SteamerDeck = 0x3034,

    /// <summary>Signalling Feena cutscene (<c>3038</c>).</summary>
    SignallingFeena = 0x3038,

    /// <summary>Sailing on cutscene (<c>3039</c>).</summary>
    SailingOn = 0x3039,

    /// <summary>Arriving at New Parm cutscene (<c>303A</c>).</summary>
    ArrivingAtNewParm = 0x303A,

    /// <summary>Ghost Ship deck (odd camera; possible cutscene map) (<c>3400</c>).</summary>
    GhostShipDeck = 0x3400,

    /// <summary>Ghost Ship upper deck (<c>3404</c>).</summary>
    GhostShipUpperDeck = 0x3404,

    /// <summary>Ghost Ship upper deck sinking (cutscene) (<c>3405</c>).</summary>
    GhostShipUpperDeckSinking = 0x3405,

    /// <summary>Ghost Ship pantry (<c>3408</c>).</summary>
    GhostShipPantry = 0x3408,

    /// <summary>Ghost Ship hold (<c>340C</c>).</summary>
    GhostShipHold = 0x340C,

    /// <summary>Ghost Ship hold (upper) (<c>340D</c>).</summary>
    GhostShipHoldUpper = 0x340D,

    /// <summary>Ghost Ship bottom (<c>3410</c>).</summary>
    GhostShipBottom = 0x3410,

    /// <summary>Ghost Ship treasure room (<c>3414</c>).</summary>
    GhostShipTreasureRoom = 0x3414,

    /// <summary>Ghost Ship lower deck (<c>341C</c>).</summary>
    GhostShipLowerDeck = 0x341C,

    /// <summary>Ghost Ship mid deck (<c>3420</c>).</summary>
    GhostShipMidDeck = 0x3420,

    /// <summary>Ghost Ship hall (<c>3424</c>).</summary>
    GhostShipHall = 0x3424,

    /// <summary>Ghost Ship captain's cabin (<c>3428</c>).</summary>
    GhostShipCaptainsCabin = 0x3428,

    /// <summary>Port of New Parm (<c>3800</c>).</summary>
    PortOfNewParm = 0x3800,

    /// <summary>Port info desk (<c>3804</c>).</summary>
    PortInfoDesk = 0x3804,

    /// <summary>Town of New Parm (<c>3C00</c>).</summary>
    NewParm = 0x3C00,

    /// <summary>Town of New Parm (again) (<c>3C02</c>).</summary>
    NewParmAlt = 0x3C02,

    /// <summary>New Parm inn (<c>3C04</c>).</summary>
    NewParmInn = 0x3C04,

    /// <summary>New Parm shop (<c>3C08</c>).</summary>
    NewParmShop = 0x3C08,

    /// <summary>New Parm dinner theater (<c>3C0C</c>).</summary>
    NewParmDinnerTheater = 0x3C0C,

    /// <summary>New Parm church (<c>3C10</c>).</summary>
    NewParmChurch = 0x3C10,

    /// <summary>Adventurer's Society (<c>3C14</c>).</summary>
    AdventurersSociety = 0x3C14,

    /// <summary>Pakon's office (<c>3C18</c>).</summary>
    PakonsOffice = 0x3C18,

    /// <summary>New Parm mansion 1 (<c>3C1C</c>).</summary>
    NewParmMansion1 = 0x3C1C,

    /// <summary>New Parm mansion 2 (<c>3C20</c>).</summary>
    NewParmMansion2 = 0x3C20,

    /// <summary>New Parm mansion 3 (<c>3C24</c>).</summary>
    NewParmMansion3 = 0x3C24,

    /// <summary>New Parm house 1 (<c>3C28</c>).</summary>
    NewParmHouse1 = 0x3C28,

    /// <summary>New Parm house 2 (<c>3C2C</c>).</summary>
    NewParmHouse2 = 0x3C2C,

    /// <summary>New Parm house 3 (<c>3C30</c>).</summary>
    NewParmHouse3 = 0x3C30,

    /// <summary>New Parm house 4 (<c>3C34</c>).</summary>
    NewParmHouse4 = 0x3C34,

    /// <summary>New Parm house 5 (<c>3C38</c>).</summary>
    NewParmHouse5 = 0x3C38,

    /// <summary>New Parm house 6 (<c>3C3C</c>).</summary>
    NewParmHouse6 = 0x3C3C,

    /// <summary>New Parm pawn shop (<c>3C40</c>).</summary>
    NewParmPawnShop = 0x3C40,

    /// <summary>New Parm house 7 (<c>3C44</c>).</summary>
    NewParmHouse7 = 0x3C44,

    /// <summary>Underground passage entrance (<c>3C54</c>).</summary>
    UndergroundPassageEntrance = 0x3C54,

    /// <summary>Underground passage (<c>3C58</c>).</summary>
    UndergroundPassage = 0x3C58,

    /// <summary>Feena's house (outside) (<c>4000</c>).</summary>
    FeenasHouse = 0x4000,

    /// <summary>Inside Feena's house (almost all) (<c>4004</c>).</summary>
    InsideFeenasHouse = 0x4004,

    /// <summary>Inside Feena's house (with Rem and Sulfa weed) (<c>4005</c>).</summary>
    InsideFeenasHouseRem = 0x4005,

    /// <summary>Herb Mountains (<c>4008</c>).</summary>
    HerbMountains = 0x4008,

    /// <summary>Merrill Road (<c>400C</c>).</summary>
    MerrillRoad = 0x400C,

    /// <summary>Dom vestibule (<c>4400</c>).</summary>
    DomVestibule = 0x4400,

    /// <summary>Dom Ruins 1 (<c>4404</c>).</summary>
    DomRuins1 = 0x4404,

    /// <summary>Dom cliff (<c>4408</c>).</summary>
    DomCliff = 0x4408,

    /// <summary>Dom Ruins 2 (<c>440C</c>).</summary>
    DomRuins2 = 0x440C,

    /// <summary>Dom Ruins Room of Illusion (<c>4410</c>).</summary>
    DomRuinsRoomOfIllusion = 0x4410,

    /// <summary>Road to Dom Ruins (<c>4414</c>).</summary>
    RoadToDomRuins = 0x4414,

    /// <summary>West Misty Forest (with train cutscene) (<c>4804</c>).</summary>
    WestMistyForest = 0x4804,

    /// <summary>East Misty Forest 2 (<c>4808</c>).</summary>
    EastMistyForest2 = 0x4808,

    /// <summary>East Misty Forest 3 (<c>480C</c>).</summary>
    EastMistyForest3 = 0x480C,

    /// <summary>Looking up at End of the World (<c>480D</c>).</summary>
    LookingUpAtEndOfTheWorld = 0x480D,

    /// <summary>East Misty Forest 1 (<c>4810</c>).</summary>
    EastMistyForest1 = 0x4810,

    /// <summary>Where the God of Light fell (cutscene) (<c>4814</c>).</summary>
    WhereTheGodOfLightFell = 0x4814,

    /// <summary>Garlyle Base entrance (cutscene) (<c>4C04</c>).</summary>
    GarlyleBaseEntrance = 0x4C04,

    /// <summary>Garlyle Base (cutscene) (<c>4C08</c>).</summary>
    GarlyleBase = 0x4C08,

    /// <summary>Garlyle Base (night) (<c>4C0A</c>).</summary>
    GarlyleBaseNight = 0x4C0A,

    /// <summary>Garlyle barracks (<c>4C0C</c>).</summary>
    GarlyleBarracks = 0x4C0C,

    /// <summary>Garlyle jail (<c>4C10</c>).</summary>
    GarlyleJail = 0x4C10,

    /// <summary>Garlyle warehouse (<c>4C18</c>).</summary>
    GarlyleWarehouse = 0x4C18,

    /// <summary>Garlyle locker room (<c>4C1C</c>).</summary>
    GarlyleLockerRoom = 0x4C1C,

    /// <summary>Garlyle train switchyard (<c>4C20</c>).</summary>
    GarlyleTrainSwitchyard = 0x4C20,

    /// <summary>Garlyle Mullen's room (cutscene) (<c>4C24</c>).</summary>
    GarlyleMullensRoom = 0x4C24,

    /// <summary>Train intro (<c>5000</c>).</summary>
    TrainIntro = 0x5000,

    /// <summary>Train attacked cutscene (<c>5002</c>).</summary>
    TrainAttacked = 0x5002,

    /// <summary>Train front cabin (<c>5004</c>).</summary>
    TrainFrontCabin = 0x5004,

    /// <summary>Luc Village (<c>5400</c>).</summary>
    LucVillage = 0x5400,

    /// <summary>Luc Village (night) (<c>5402</c>).</summary>
    LucVillageNight = 0x5402,

    /// <summary>Rem's house (<c>5404</c>).</summary>
    RemsHouse = 0x5404,

    /// <summary>Rem's house (night) (<c>5405</c>).</summary>
    RemsHouseNight = 0x5405,

    /// <summary>Luc chief's house (<c>5408</c>).</summary>
    LucChiefsHouse = 0x5408,

    /// <summary>Luc chief's house (night) (<c>5409</c>).</summary>
    LucChiefsHouseNight = 0x5409,

    /// <summary>Luc house 1 (<c>540C</c>).</summary>
    LucHouse1 = 0x540C,

    /// <summary>Luc house 1 (night) (<c>540D</c>).</summary>
    LucHouse1Night = 0x540D,

    /// <summary>Luc house 2 (<c>5410</c>).</summary>
    LucHouse2 = 0x5410,

    /// <summary>Luc house 2 (night) (<c>5411</c>).</summary>
    LucHouse2Night = 0x5411,

    /// <summary>Luc store (<c>5414</c>).</summary>
    LucStore = 0x5414,

    /// <summary>Luc store (night) (<c>5415</c>).</summary>
    LucStoreNight = 0x5415,

    /// <summary>Luc house 4 (<c>5418</c>).</summary>
    LucHouse4 = 0x5418,

    /// <summary>Luc house 4 (night) (<c>5419</c>).</summary>
    LucHouse4Night = 0x5419,

    /// <summary>Luc house 3 (<c>541C</c>).</summary>
    LucHouse3 = 0x541C,

    /// <summary>Luc house 3 (night) (<c>541D</c>).</summary>
    LucHouse3Night = 0x541D,

    /// <summary>God of Light Mountain (foot) (<c>5800</c>).</summary>
    GodOfLightMountainFoot = 0x5800,

    /// <summary>God of Light Mountain (foot, night) (<c>5802</c>).</summary>
    GodOfLightMountainFootNight = 0x5802,

    /// <summary>God of Light Mountain (peak) (<c>5808</c>).</summary>
    GodOfLightMountainPeak = 0x5808,

    /// <summary>God of Light Mountain (peak, night) (<c>580A</c>).</summary>
    GodOfLightMountainPeakNight = 0x580A,

    /// <summary>West Rangle Mountains (<c>5C00</c>).</summary>
    WestRangleMountains = 0x5C00,

    /// <summary>East Rangle Mountains (<c>5C04</c>).</summary>
    EastRangleMountains = 0x5C04,

    /// <summary>Rangle cutscene (<c>5C05</c>).</summary>
    RangleCutscene = 0x5C05,

    /// <summary>End of the World 1 (<c>6004</c>).</summary>
    EndOfTheWorld1 = 0x6004,

    /// <summary>End of the World 2 (<c>6008</c>).</summary>
    EndOfTheWorld2 = 0x6008,

    /// <summary>End of the World 3 (<c>600C</c>).</summary>
    EndOfTheWorld3 = 0x600C,

    /// <summary>End of the World 4 (<c>6010</c>).</summary>
    EndOfTheWorld4 = 0x6010,

    /// <summary>End of the World 5 (<c>6014</c>).</summary>
    EndOfTheWorld5 = 0x6014,

    /// <summary>End of the World 6 (<c>6018</c>).</summary>
    EndOfTheWorld6 = 0x6018,

    /// <summary>End of the World 7 (<c>601C</c>).</summary>
    EndOfTheWorld7 = 0x601C,

    /// <summary>End of the World 8 (<c>6020</c>).</summary>
    EndOfTheWorld8 = 0x6020,

    /// <summary>End of the World 9 (<c>6024</c>).</summary>
    EndOfTheWorld9 = 0x6024,

    /// <summary>End of the World 10 (<c>6028</c>).</summary>
    EndOfTheWorld10 = 0x6028,

    /// <summary>End of the World cutscene (<c>602A</c>).</summary>
    EndOfTheWorldCutscene = 0x602A,

    /// <summary>Valley of the Flying Dragon 1 (<c>6400</c>).</summary>
    ValleyOfFlyingDragon1 = 0x6400,

    /// <summary>Valley of the Flying Dragon cutscene (<c>6401</c>).</summary>
    ValleyOfFlyingDragonCutscene = 0x6401,

    /// <summary>Valley of the Flying Dragon 2 (<c>6404</c>).</summary>
    ValleyOfFlyingDragon2 = 0x6404,

    /// <summary>Valley of the Flying Dragon 3 (<c>6408</c>).</summary>
    ValleyOfFlyingDragon3 = 0x6408,

    /// <summary>Valley of the Flying Dragon 4 (<c>6410</c>).</summary>
    ValleyOfFlyingDragon4 = 0x6410,

    /// <summary>Gadwin's house (<c>6414</c>).</summary>
    GadwinsHouse = 0x6414,

    /// <summary>Dight Village (beginning) (<c>6800</c>).</summary>
    DightVillage = 0x6800,

    /// <summary>Dight Village (after spear) (<c>6801</c>).</summary>
    DightVillageAfterSpear = 0x6801,

    /// <summary>Dight Village (after Twin Towers) (<c>6802</c>).</summary>
    DightVillageAfterTwinTowers = 0x6802,

    /// <summary>Dight inn (<c>6804</c>).</summary>
    DightInn = 0x6804,

    /// <summary>Dight shop (<c>6808</c>).</summary>
    DightShop = 0x6808,

    /// <summary>Dight elder's house (<c>680C</c>).</summary>
    DightEldersHouse = 0x680C,

    /// <summary>Dight Alma's clinic (<c>6810</c>).</summary>
    DightAlmasClinic = 0x6810,

    /// <summary>Dight house 1 (<c>6818</c>).</summary>
    DightHouse1 = 0x6818,

    /// <summary>Dight house 2 (<c>681C</c>).</summary>
    DightHouse2 = 0x681C,

    /// <summary>Dight house 3 (<c>6820</c>).</summary>
    DightHouse3 = 0x6820,

    /// <summary>Dight house 4 (<c>6824</c>).</summary>
    DightHouse4 = 0x6824,

    /// <summary>Dight house 5 (<c>682C</c>).</summary>
    DightHouse5 = 0x682C,

    /// <summary>Dight house 6 (<c>6834</c>).</summary>
    DightHouse6 = 0x6834,

    /// <summary>Mt. Typhoon (<c>6C00</c>).</summary>
    MtTyphoon = 0x6C00,

    /// <summary>Typhoon Tower collapse (<c>6C02</c>).</summary>
    TyphoonTowerCollapse = 0x6C02,

    /// <summary>Mt. Typhoon (peak) (<c>6C04</c>).</summary>
    MtTyphoonPeak = 0x6C04,

    /// <summary>Typhoon Tower 1 (<c>6C08</c>).</summary>
    TyphoonTower1 = 0x6C08,

    /// <summary>Typhoon Tower 2 (<c>6C0C</c>).</summary>
    TyphoonTower2 = 0x6C0C,

    /// <summary>Typhoon Tower (Room of Rain) (<c>6C10</c>).</summary>
    TyphoonTowerRoomOfRain = 0x6C10,

    /// <summary>Typhoon Tower (Room of Destiny) (<c>6C14</c>).</summary>
    TyphoonTowerRoomOfDestiny = 0x6C14,

    /// <summary>Typhoon peak (no rain) (<c>6C18</c>).</summary>
    TyphoonPeakNoRain = 0x6C18,

    /// <summary>Typhoon Tower (Hall of Illusions) (<c>6C1C</c>).</summary>
    TyphoonTowerHallOfIllusions = 0x6C1C,

    /// <summary>North Lama Mountains (<c>7000</c>).</summary>
    NorthLamaMountains = 0x7000,

    /// <summary>South Lama Mountains (<c>7004</c>).</summary>
    SouthLamaMountains = 0x7004,

    /// <summary>Gumbo Village (<c>7400</c>).</summary>
    GumboVillage = 0x7400,

    /// <summary>Gumbo Village (post-volcano) (<c>7401</c>).</summary>
    GumboVillagePostVolcano = 0x7401,

    /// <summary>Gumbo Village (night) (<c>7402</c>).</summary>
    GumboVillageNight = 0x7402,

    /// <summary>Gumbo Village (Justin and Feena cutscene) (<c>7403</c>).</summary>
    GumboJustinAndFeena = 0x7403,

    /// <summary>Gumbo inn (<c>7404</c>).</summary>
    GumboInn = 0x7404,

    /// <summary>Gumbo inn (no music) (<c>7405</c>).</summary>
    GumboInnNoMusic = 0x7405,

    /// <summary>Gumbo weapon store (<c>7408</c>).</summary>
    GumboWeaponStore = 0x7408,

    /// <summary>Gumbo chief's house (<c>740C</c>).</summary>
    GumboChiefsHouse = 0x740C,

    /// <summary>Gumbo dining hall (<c>7410</c>).</summary>
    GumboDiningHall = 0x7410,

    /// <summary>Gumbo house 1 (<c>7414</c>).</summary>
    GumboHouse1 = 0x7414,

    /// <summary>Gumbo house 2 (<c>7418</c>).</summary>
    GumboHouse2 = 0x7418,

    /// <summary>Gumbo house 3 (<c>741C</c>).</summary>
    GumboHouse3 = 0x741C,

    /// <summary>Gumbo house 4 (<c>7420</c>).</summary>
    GumboHouse4 = 0x7420,

    /// <summary>Gumbo greeting tent (nighttime festival) (<c>7424</c>).</summary>
    GumboGreetingTentFestival = 0x7424,

    /// <summary>Gumbo greeting tent (all others) (<c>7425</c>).</summary>
    GumboGreetingTent = 0x7425,

    /// <summary>Gumbo house 5 (<c>7428</c>).</summary>
    GumboHouse5 = 0x7428,

    /// <summary>Being catapulted cutscene (<c>742B</c>).</summary>
    BeingCatapulted = 0x742B,

    /// <summary>Upside-down cutscene (<c>742C</c>).</summary>
    UpsideDown = 0x742C,

    /// <summary>Volcano slope 1 (<c>7800</c>).</summary>
    VolcanoSlope1 = 0x7800,

    /// <summary>Volcano slope 2 (<c>7804</c>).</summary>
    VolcanoSlope2 = 0x7804,

    /// <summary>Leaving volcano cutscene (<c>7805</c>).</summary>
    LeavingVolcano = 0x7805,

    /// <summary>Volcano map that crashes some emulators (opcode 37) (<c>7806</c>).</summary>
    VolcanoCrash = 0x7806,

    /// <summary>Erupting volcano cutscene (<c>7807</c>).</summary>
    EruptingVolcano = 0x7807,

    /// <summary>Volcano crater (<c>7808</c>).</summary>
    VolcanoCrater = 0x7808,

    /// <summary>Volcano base (<c>780C</c>).</summary>
    VolcanoBase = 0x780C,

    /// <summary>Twin Towers vestibule (<c>7C00</c>).</summary>
    TwinTowersVestibule = 0x7C00,

    /// <summary>Twin Towers (west) (<c>7C04</c>).</summary>
    TwinTowersWest = 0x7C04,

    /// <summary>Twin Towers (east) (<c>7C08</c>).</summary>
    TwinTowersEast = 0x7C08,

    /// <summary>Twin Towers (north) (<c>7C0C</c>).</summary>
    TwinTowersNorth = 0x7C0C,

    /// <summary>Twin Towers (south) (<c>7C10</c>).</summary>
    TwinTowersSouth = 0x7C10,

    /// <summary>Twin Towers (Room of Original Sin) (<c>7C18</c>).</summary>
    TwinTowersRoomOfOriginalSin = 0x7C18,

    /// <summary>Twin Towers (Feena and Mullen; IDed as Underground) (<c>7C1C</c>).</summary>
    TwinTowersFeenaAndMullen = 0x7C1C,

    /// <summary>Twin Towers wet room (IDed as Upper Hall) (<c>7C20</c>).</summary>
    TwinTowersWetRoom = 0x7C20,

    /// <summary>Twin Towers (Hall of Murals) (<c>7C28</c>).</summary>
    TwinTowersHallOfMurals = 0x7C28,

    /// <summary>Twin Towers spiral staircase (unused) (<c>7C30</c>).</summary>
    TwinTowersSpiralStaircase = 0x7C30,

    /// <summary>Twin Towers (Room of Illusion) (<c>7C34</c>).</summary>
    TwinTowersRoomOfIllusion = 0x7C34,

    /// <summary>Twin Towers (Room of Temptation) (<c>7C3C</c>).</summary>
    TwinTowersRoomOfTemptation = 0x7C3C,

    /// <summary>Twin Towers beach (<c>7C44</c>).</summary>
    TwinTowersBeach = 0x7C44,

    /// <summary>Mysterious Vanishing Hill (<c>8004</c>).</summary>
    MysteriousVanishingHill = 0x8004,

    /// <summary>Mysterious Vanishing Shrine (<c>8008</c>).</summary>
    MysteriousVanishingShrine = 0x8008,

    /// <summary>Vanishing Hill shrine (with Parm) (<c>800A</c>).</summary>
    VanishingHillShrineWithParm = 0x800A,

    /// <summary>Outside Sea Dragon (no ocean). Same stem on disc 2 (<c>8800</c>).</summary>
    OutsideSeaDragon = 0x8800,

    /// <summary>
    /// Sea Dragon night wide shot (<c>8801</c>). Disc 1 leads toward the
    /// lifeboat; disc 2 is the drifting-at-night cutscene.
    /// </summary>
    SeaDragonNightWide = 0x8801,

    /// <summary>
    /// Outside Sea Dragon at night (<c>8802</c>). Disc 2 is the Justin and
    /// Feena moment.
    /// </summary>
    OutsideSeaDragonNight = 0x8802,

    /// <summary>
    /// Sea Dragon night overhead (<c>8803</c>). Disc 2 zooms in on Feena.
    /// </summary>
    SeaDragonNightOverhead = 0x8803,

    /// <summary>Inside the Sea Dragon (<c>8804</c>).</summary>
    InsideSeaDragon = 0x8804,

    /// <summary>
    /// Across the Sea of Mermaids / Sea Dragon toward Virgin Forest
    /// (<c>8808</c>). Disc-change prompt on some builds.
    /// </summary>
    AcrossSeaOfMermaids = 0x8808,

    /// <summary>Mermaid Island (<c>8C00</c>). Disc 2 file matches disc 1.</summary>
    MermaidIsland = 0x8C00,

    /// <summary>Pirate hideout (<c>8C04</c>). Disc 2 file matches disc 1.</summary>
    PirateHideout = 0x8C04,

    /// <summary>Baal intro (Grandeur upper bridge / Parm backdrop) (<c>BA38</c>).</summary>
    BaalIntro = 0xBA38,

    /// <summary>Virgin Forest 1 (<c>9000</c>).</summary>
    VirginForest1 = 0x9000,

    /// <summary>Virgin Forest 2 (<c>9004</c>).</summary>
    VirginForest2 = 0x9004,

    /// <summary>Virgin Forest 3 (<c>9008</c>).</summary>
    VirginForest3 = 0x9008,

    /// <summary>Virgin Forest 4 (<c>900C</c>).</summary>
    VirginForest4 = 0x900C,

    /// <summary>Guido's tent (<c>9010</c>).</summary>
    GuidosTent = 0x9010,

    /// <summary>Cafu Village (<c>9400</c>).</summary>
    CafuVillage = 0x9400,

    /// <summary>Cafu (night) (<c>9402</c>).</summary>
    CafuNight = 0x9402,

    /// <summary>Cafu inn (<c>9404</c>).</summary>
    CafuInn = 0x9404,

    /// <summary>Cafu inn (night) (<c>9405</c>).</summary>
    CafuInnNight = 0x9405,

    /// <summary>Cafu store (<c>9408</c>).</summary>
    CafuStore = 0x9408,

    /// <summary>Cafu store (night) (<c>9409</c>).</summary>
    CafuStoreNight = 0x9409,

    /// <summary>Cafu elder's house, second floor (<c>940C</c>).</summary>
    CafuEldersHouseSecondFloor = 0x940C,

    /// <summary>Cafu elder's house, first floor (<c>9410</c>).</summary>
    CafuEldersHouseFirstFloor = 0x9410,

    /// <summary>Cafu house 2, second floor (<c>9412</c>).</summary>
    CafuHouse2SecondFloor = 0x9412,

    /// <summary>Cafu house 4, first floor (<c>9414</c>).</summary>
    CafuHouse4FirstFloor = 0x9414,

    /// <summary>Cafu house 4, second floor (<c>9416</c>).</summary>
    CafuHouse4SecondFloor = 0x9416,

    /// <summary>Cafu house 5 (<c>9418</c>).</summary>
    CafuHouse5 = 0x9418,

    /// <summary>Cafu house 6 (<c>941C</c>).</summary>
    CafuHouse6 = 0x941C,

    /// <summary>Cafu house 2, first floor (<c>9420</c>).</summary>
    CafuHouse2FirstFloor = 0x9420,

    /// <summary>Cafu house 1, first floor (<c>9424</c>).</summary>
    CafuHouse1FirstFloor = 0x9424,

    /// <summary>Cafu house 2, second floor (alt) (<c>9426</c>).</summary>
    CafuHouse2SecondFloorAlt = 0x9426,

    /// <summary>Cafu house 3, first floor (<c>9428</c>).</summary>
    CafuHouse3FirstFloor = 0x9428,

    /// <summary>Cafu house 3, second floor (<c>942C</c>).</summary>
    CafuHouse3SecondFloor = 0x942C,

    /// <summary>Home Tree plaza (<c>9430</c>).</summary>
    HomeTreePlaza = 0x9430,

    /// <summary>Home Tree plaza (night) (<c>9431</c>).</summary>
    HomeTreePlazaNight = 0x9431,

    /// <summary>Petrified Forest 1 (<c>9600</c>).</summary>
    PetrifiedForest1 = 0x9600,

    /// <summary>Petrified Forest 2 (<c>9604</c>).</summary>
    PetrifiedForest2 = 0x9604,

    /// <summary>Tower of Doom reveal (<c>9606</c>).</summary>
    TowerOfDoomReveal = 0x9606,

    /// <summary>Tower of Doom (<c>9804</c>).</summary>
    TowerOfDoom = 0x9804,

    /// <summary>Tower of Doom warehouse (<c>9805</c>).</summary>
    TowerOfDoomWarehouse = 0x9805,

    /// <summary>Tower of Doom 1 (<c>9808</c>).</summary>
    TowerOfDoom1 = 0x9808,

    /// <summary>Tower of Doom 2 (<c>980C</c>).</summary>
    TowerOfDoom2 = 0x980C,

    /// <summary>Tower of Doom 3 (<c>9810</c>).</summary>
    TowerOfDoom3 = 0x9810,

    /// <summary>Tower of Doom lab (<c>9814</c>).</summary>
    TowerOfDoomLab = 0x9814,

    /// <summary>North Zil Desert (<c>9C00</c>).</summary>
    NorthZilDesert = 0x9C00,

    /// <summary>South Zil Desert (<c>9C04</c>).</summary>
    SouthZilDesert = 0x9C04,

    /// <summary>West Savanna (<c>9C08</c>).</summary>
    WestSavanna = 0x9C08,

    /// <summary>East Savanna (<c>9C0C</c>).</summary>
    EastSavanna = 0x9C0C,

    /// <summary>North Zil Desert (Gaia) (<c>9C10</c>).</summary>
    NorthZilDesertGaia = 0x9C10,

    /// <summary>South Zil Desert (Gaia) (<c>9C14</c>).</summary>
    SouthZilDesertGaia = 0x9C14,

    /// <summary>West Savanna (Gaia) (<c>9C18</c>).</summary>
    WestSavannaGaia = 0x9C18,

    /// <summary>East Savanna (Gaia) (<c>9C1C</c>).</summary>
    EastSavannaGaia = 0x9C1C,

    /// <summary>Zil Padon (<c>A000</c>).</summary>
    ZilPadon = 0xA000,

    /// <summary>Hotel Alqada lobby (<c>A004</c>).</summary>
    HotelAlqadaLobby = 0xA004,

    /// <summary>Hotel Alqada hall (<c>A008</c>).</summary>
    HotelAlqadaHall = 0xA008,

    /// <summary>Hotel Alqada A (<c>A00C</c>).</summary>
    HotelAlqadaA = 0xA00C,

    /// <summary>Hotel Alqada B (<c>A00E</c>).</summary>
    HotelAlqadaB = 0xA00E,

    /// <summary>Zil Padon store (<c>A010</c>).</summary>
    ZilPadonStore = 0xA010,

    /// <summary>Olva's tent (<c>A014</c>).</summary>
    OlvasTent = 0xA014,

    /// <summary>Oasis (<c>A018</c>).</summary>
    Oasis = 0xA018,

    /// <summary>Alqada spice (<c>A01A</c>).</summary>
    AlqadaSpice = 0xA01A,

    /// <summary>Zil Padon house 1 (<c>A01C</c>).</summary>
    ZilPadonHouse1 = 0xA01C,

    /// <summary>Zil Padon house 2 (<c>A01E</c>).</summary>
    ZilPadonHouse2 = 0xA01E,

    /// <summary>Rafane cafe (<c>A020</c>).</summary>
    RafaneCafe = 0xA020,

    /// <summary>Apartment building hall (<c>A024</c>).</summary>
    ApartmentBuildingHall = 0xA024,

    /// <summary>Apartment A (<c>A028</c>).</summary>
    ApartmentA = 0xA028,

    /// <summary>Apartment B (<c>A02C</c>).</summary>
    ApartmentB = 0xA02C,

    /// <summary>Mogay elder's house (<c>A030</c>).</summary>
    MogayEldersHouse = 0xA030,

    /// <summary>Mogay house 1 (<c>A034</c>).</summary>
    MogayHouse1 = 0xA034,

    /// <summary>Mogay house 2 (<c>A038</c>).</summary>
    MogayHouse2 = 0xA038,

    /// <summary>Teleporter to Tower of Temptation (<c>A03A</c>).</summary>
    TeleporterToTowerOfTemptation = 0xA03A,

    /// <summary>Destroyed Zil Padon (<c>A200</c>).</summary>
    DestroyedZilPadon = 0xA200,

    /// <summary>Destroyed Zil Padon with Leen (<c>A202</c>).</summary>
    DestroyedZilPadonLeen = 0xA202,

    /// <summary>Destroyed Zil Padon post-Gaia (<c>A203</c>).</summary>
    DestroyedZilPadonPostGaia = 0xA203,

    /// <summary>Guido's house (Gaia) (<c>A230</c>).</summary>
    GuidosHouseGaia = 0xA230,

    /// <summary>Destroyed Zil Padon house 1 (<c>A234</c>).</summary>
    DestroyedZilPadonHouse1 = 0xA234,

    /// <summary>Destroyed Zil Padon inn (<c>A240</c>).</summary>
    DestroyedZilPadonInn = 0xA240,

    /// <summary>Destroyed Zil Padon shop (<c>A244</c>).</summary>
    DestroyedZilPadonShop = 0xA244,

    /// <summary>Castle of Dreams (<c>A400</c>).</summary>
    CastleOfDreams = 0xA400,

    /// <summary>Castle of Dreams great hall (<c>A404</c>).</summary>
    CastleOfDreamsGreatHall = 0xA404,

    /// <summary>Castle of Dreams cemetery (<c>A40C</c>).</summary>
    CastleOfDreamsCemetery = 0xA40C,

    /// <summary>Castle of Dreams knight's room (<c>A410</c>).</summary>
    CastleOfDreamsKnightsRoom = 0xA410,

    /// <summary>Castle of Dreams library (<c>A414</c>).</summary>
    CastleOfDreamsLibrary = 0xA414,

    /// <summary>Castle of Dreams dining hall (<c>A418</c>).</summary>
    CastleOfDreamsDiningHall = 0xA418,

    /// <summary>Castle of Dreams small room (<c>A41C</c>).</summary>
    CastleOfDreamsSmallRoom = 0xA41C,

    /// <summary>Castle of Dreams study (<c>A420</c>).</summary>
    CastleOfDreamsStudy = 0xA420,

    /// <summary>Castle of Dreams rooftop (<c>A428</c>).</summary>
    CastleOfDreamsRooftop = 0xA428,

    /// <summary>Castle of Dreams lord's chamber (<c>A42C</c>).</summary>
    CastleOfDreamsLordsChamber = 0xA42C,

    /// <summary>Laine (<c>A800</c>).</summary>
    Laine = 0xA800,

    /// <summary>Laine inn (<c>A804</c>).</summary>
    LaineInn = 0xA804,

    /// <summary>Laine shop (<c>A808</c>).</summary>
    LaineShop = 0xA808,

    /// <summary>Dorlin's house (<c>A80C</c>).</summary>
    DorlinsHouse = 0xA80C,

    /// <summary>Laine vacant house (<c>A80E</c>).</summary>
    LaineVacantHouse = 0xA80E,

    /// <summary>Milda's house (<c>A810</c>).</summary>
    MildasHouse = 0xA810,

    /// <summary>Milda's bedroom (<c>A812</c>).</summary>
    MildasBedroom = 0xA812,

    /// <summary>Laine blacksmith (<c>A814</c>).</summary>
    LaineBlacksmith = 0xA814,

    /// <summary>Laine barn (<c>A818</c>).</summary>
    LaineBarn = 0xA818,

    /// <summary>Derlin's house (<c>A81C</c>).</summary>
    DerlinsHouse = 0xA81C,

    /// <summary>Laine house 1 (<c>A820</c>).</summary>
    LaineHouse1 = 0xA820,

    /// <summary>Laine house 2 (<c>A824</c>).</summary>
    LaineHouse2 = 0xA824,

    /// <summary>Soldier's Graveyard 1 (<c>AC04</c>).</summary>
    SoldiersGraveyard1 = 0xAC04,

    /// <summary>Soldier's Graveyard 2 (<c>AC08</c>).</summary>
    SoldiersGraveyard2 = 0xAC08,

    /// <summary>Soldier's Graveyard 3 (<c>AC0C</c>).</summary>
    SoldiersGraveyard3 = 0xAC0C,

    /// <summary>Soldier's Graveyard 4 west (<c>AC10</c>).</summary>
    SoldiersGraveyard4West = 0xAC10,

    /// <summary>Soldier's Graveyard 4 east (<c>AC14</c>).</summary>
    SoldiersGraveyard4East = 0xAC14,

    /// <summary>Soldier's Graveyard 5 (<c>AC18</c>).</summary>
    SoldiersGraveyard5 = 0xAC18,

    /// <summary>Tower of Temptation entrance (<c>B400</c>).</summary>
    TowerOfTemptationEntrance = 0xB400,

    /// <summary>Tower of Temptation top floor (<c>B404</c>).</summary>
    TowerOfTemptationTopFloor = 0xB404,

    /// <summary>Tower of Temptation 1 (<c>B408</c>).</summary>
    TowerOfTemptation1 = 0xB408,

    /// <summary>Tower of Temptation 2 (<c>B40C</c>).</summary>
    TowerOfTemptation2 = 0xB40C,

    /// <summary>Tower of Temptation 3 (<c>B410</c>).</summary>
    TowerOfTemptation3 = 0xB410,

    /// <summary>Tower of Temptation rampart 1 (<c>B414</c>).</summary>
    TowerOfTemptationRampart1 = 0xB414,

    /// <summary>Tower of Temptation 4 (<c>B418</c>).</summary>
    TowerOfTemptation4 = 0xB418,

    /// <summary>Tower of Temptation 5 (<c>B41C</c>).</summary>
    TowerOfTemptation5 = 0xB41C,

    /// <summary>Tower of Temptation 6 (<c>B420</c>).</summary>
    TowerOfTemptation6 = 0xB420,

    /// <summary>Tower of Temptation 7 (<c>B424</c>).</summary>
    TowerOfTemptation7 = 0xB424,

    /// <summary>Tower of Temptation 8 (<c>B428</c>).</summary>
    TowerOfTemptation8 = 0xB428,

    /// <summary>Tower of Temptation 9 (<c>B42C</c>).</summary>
    TowerOfTemptation9 = 0xB42C,

    /// <summary>Tower of Temptation 10 (<c>B430</c>).</summary>
    TowerOfTemptation10 = 0xB430,

    /// <summary>Tower of Temptation 11 (<c>B434</c>).</summary>
    TowerOfTemptation11 = 0xB434,

    /// <summary>Tower of Temptation 12 (<c>B438</c>).</summary>
    TowerOfTemptation12 = 0xB438,

    /// <summary>Tower of Temptation rampart 2 (<c>B43C</c>).</summary>
    TowerOfTemptationRampart2 = 0xB43C,

    /// <summary>Underground Ruins 1 (<c>B800</c>).</summary>
    UndergroundRuins1 = 0xB800,

    /// <summary>Underground Ruins 2 (<c>B804</c>).</summary>
    UndergroundRuins2 = 0xB804,

    /// <summary>Underground Ruins 3 (<c>B806</c>).</summary>
    UndergroundRuins3 = 0xB806,

    /// <summary>Underground Ruins 4 (<c>B808</c>).</summary>
    UndergroundRuins4 = 0xB808,

    /// <summary>Underground Ruins shrine 1 (<c>B80C</c>).</summary>
    UndergroundRuinsShrine1 = 0xB80C,

    /// <summary>Underground Ruins shrine 2 (<c>B810</c>).</summary>
    UndergroundRuinsShrine2 = 0xB810,

    /// <summary>Underground Ruins B3 (<c>B814</c>).</summary>
    UndergroundRuinsB3 = 0xB814,

    /// <summary>Underground Ruins B2 (<c>B818</c>).</summary>
    UndergroundRuinsB2 = 0xB818,

    /// <summary>Underground Ruins B1 (<c>B81C</c>).</summary>
    UndergroundRuinsB1 = 0xB81C,

    /// <summary>Leen and Baal (<c>B820</c>).</summary>
    LeenAndBaal = 0xB820,

    /// <summary>Underground Ruins pre-exit (<c>B824</c>).</summary>
    UndergroundRuinsPreExit = 0xB824,

    /// <summary>Underground Ruins exit (<c>B828</c>).</summary>
    UndergroundRuinsExit = 0xB828,

    /// <summary>Outside ancient ruins, Grandeur (<c>B82C</c>).</summary>
    OutsideAncientRuinsGrandeur = 0xB82C,

    /// <summary>Baal kidnaps Feena (<c>B830</c>).</summary>
    BaalKidnapsFeena = 0xB830,

    /// <summary>Approaching the Grandeur cutscene (<c>BA00</c>).</summary>
    ApproachingTheGrandeur = 0xBA00,

    /// <summary>Grandeur outside (<c>BA04</c>).</summary>
    GrandeurOutside = 0xBA04,

    /// <summary>Grandeur passageway (<c>BA08</c>).</summary>
    GrandeurPassageway = 0xBA08,

    /// <summary>Grandeur engine room (<c>BA0C</c>).</summary>
    GrandeurEngineRoom = 0xBA0C,

    /// <summary>Grandeur control room (stuck in floor) (<c>BA10</c>).</summary>
    GrandeurControlRoom = 0xBA10,

    /// <summary>Baal's quarters (<c>BA18</c>).</summary>
    BaalsQuarters = 0xBA18,

    /// <summary>Mullen's quarters, Lyonlot (<c>BA1A</c>).</summary>
    MullensQuartersLyonlot = 0xBA1A,

    /// <summary>Grandeur passageway (second) (<c>BA20</c>).</summary>
    GrandeurPassageway2 = 0xBA20,

    /// <summary>Grandeur secret passageway (<c>BA28</c>).</summary>
    GrandeurSecretPassageway = 0xBA28,

    /// <summary>Grandeur outside bow, but inside (<c>BA2C</c>).</summary>
    GrandeurOutsideBowInside = 0xBA2C,

    /// <summary>Grandeur outside bow (<c>BA30</c>).</summary>
    GrandeurOutsideBow = 0xBA30,

    /// <summary>Grandeur command center (<c>BA34</c>).</summary>
    GrandeurCommandCenter = 0xBA34,

    /// <summary>Justin in a pit (<c>BA36</c>).</summary>
    JustinInAPit = 0xBA36,

    /// <summary>Grandeur throne room (<c>BA3A</c>).</summary>
    GrandeurThroneRoom = 0xBA3A,

    /// <summary>Grandeur catapult (Feena and Baal cutscene) (<c>BA3C</c>).</summary>
    GrandeurCatapultFeenaBaal = 0xBA3C,

    /// <summary>Grandeur catapult (no sprites; battle background corrupt) (<c>BA3E</c>).</summary>
    GrandeurCatapult = 0xBA3E,

    /// <summary>Icarian Leen cutscene 1 (<c>BA40</c>).</summary>
    IcarianLeen1 = 0xBA40,

    /// <summary>Icarian Leen cutscene 2 (<c>BA42</c>).</summary>
    IcarianLeen2 = 0xBA42,

    /// <summary>Grandeur goes down (<c>BA44</c>).</summary>
    GrandeurGoesDown = 0xBA44,

    /// <summary>Justin and Feena fall (<c>BA48</c>).</summary>
    JustinAndFeenaFall = 0xBA48,

    /// <summary>Peak of Rainbow Mountain (<c>BC00</c>).</summary>
    PeakOfRainbowMountain = 0xBC00,

    /// <summary>Base of Rainbow Mountain (<c>BC04</c>).</summary>
    BaseOfRainbowMountain = 0xBC04,

    /// <summary>Alent (Holy Garden) (<c>C000</c>).</summary>
    AlentHolyGarden = 0xC000,

    /// <summary>Alent (<c>C004</c>).</summary>
    Alent = 0xC004,

    /// <summary>Shrine of Alent (Papal Hall) (<c>C008</c>).</summary>
    ShrineOfAlentPapalHall = 0xC008,

    /// <summary>Shrine of Alent (Knowledge Room) (<c>C00C</c>).</summary>
    ShrineOfAlentKnowledgeRoom = 0xC00C,

    /// <summary>Alent gondola (<c>C010</c>).</summary>
    AlentGondola = 0xC010,

    /// <summary>Warp Space 1 (<c>C400</c>).</summary>
    WarpSpace1 = 0xC400,

    /// <summary>Warp Space 2 (<c>C404</c>).</summary>
    WarpSpace2 = 0xC404,

    /// <summary>Warp Space 3 (<c>C408</c>).</summary>
    WarpSpace3 = 0xC408,

    /// <summary>Warp Space core (<c>C40C</c>).</summary>
    WarpSpaceCore = 0xC40C,

    /// <summary>North Brinan Plateau (<c>C800</c>).</summary>
    NorthBrinanPlateau = 0xC800,

    /// <summary>Brinan Plateau tent (<c>C802</c>).</summary>
    BrinanPlateauTent = 0xC802,

    /// <summary>South Brinan Plateau (<c>C804</c>).</summary>
    SouthBrinanPlateau = 0xC804,

    /// <summary>West Luzet Mountains (<c>CA00</c>).</summary>
    WestLuzetMountains = 0xCA00,

    /// <summary>East Luzet Mountains (<c>CA04</c>).</summary>
    EastLuzetMountains = 0xCA04,

    /// <summary>West Luzet Mountains (Gaia) (<c>CA08</c>).</summary>
    WestLuzetMountainsGaia = 0xCA08,

    /// <summary>East Luzet Mountains (Gaia) (<c>CA0C</c>).</summary>
    EastLuzetMountainsGaia = 0xCA0C,

    /// <summary>J Base (stuck in a wall) (<c>CC00</c>).</summary>
    JBaseStuck = 0xCC00,

    /// <summary>J Base hangar (<c>CC04</c>).</summary>
    JBaseHangar = 0xCC04,

    /// <summary>J Base (<c>CC06</c>).</summary>
    JBase = 0xCC06,

    /// <summary>J Base officer's quarters (<c>CC08</c>).</summary>
    JBaseOfficersQuarters = 0xCC08,

    /// <summary>J Base control room (<c>CC0A</c>).</summary>
    JBaseControlRoom = 0xCC0A,

    /// <summary>J Base cutscene (<c>CC0C</c>).</summary>
    JBaseCutscene = 0xCC0C,

    /// <summary>Gaia hatched (<c>CC0D</c>).</summary>
    GaiaHatched = 0xCC0D,

    /// <summary>J Base secret passage (<c>CC10</c>).</summary>
    JBaseSecretPassage = 0xCC10,

    /// <summary>J Base stairway (<c>CC14</c>).</summary>
    JBaseStairway = 0xCC14,

    /// <summary>J Base control room (stuck in background) (<c>CC18</c>).</summary>
    JBaseControlRoomStuck = 0xCC18,

    /// <summary>J Base control room (<c>CC1A</c>).</summary>
    JBaseControlRoom2 = 0xCC1A,

    /// <summary>J Base steam cannon room (<c>CC1C</c>).</summary>
    JBaseSteamCannonRoom = 0xCC1C,

    /// <summary>J Base steam cannon room (no power, stuck in floor) (<c>CC1D</c>).</summary>
    JBaseSteamCannonRoomNoPower = 0xCC1D,

    /// <summary>J Base roof (stuck) (<c>CC20</c>).</summary>
    JBaseRoof = 0xCC20,

    /// <summary>Underground Railway 1 (<c>D000</c>).</summary>
    UndergroundRailway1 = 0xD000,

    /// <summary>Gaia ovarian chamber (<c>D004</c>).</summary>
    GaiaOvarianChamber = 0xD004,

    /// <summary>Underground Railway 2 (<c>D008</c>).</summary>
    UndergroundRailway2 = 0xD008,

    /// <summary>Underground Railway 3 (<c>D00C</c>).</summary>
    UndergroundRailway3 = 0xD00C,

    /// <summary>Field Base 1 (<c>D400</c>).</summary>
    FieldBase1 = 0xD400,

    /// <summary>Field Base 2 (<c>D404</c>).</summary>
    FieldBase2 = 0xD404,

    /// <summary>Field Base TACOM center (<c>D408</c>).</summary>
    FieldBaseTacomCenter = 0xD408,

    /// <summary>Field Base store (<c>D40C</c>).</summary>
    FieldBaseStore = 0xD40C,

    /// <summary>Field Base officer's tent (<c>D410</c>).</summary>
    FieldBaseOfficersTent = 0xD410,

    /// <summary>Unstable map (emulator crash, SPEC opcode 28) (<c>D800</c>).</summary>
    CrashD800 = 0xD800,

    /// <summary>Unstable map (crash) (<c>D804</c>).</summary>
    CrashD804 = 0xD804,

    /// <summary>Spirit Sanctuary (<c>D808</c>).</summary>
    SpiritSanctuary = 0xD808,

    /// <summary>Spirit Sanctuary final room (<c>D80C</c>).</summary>
    SpiritSanctuaryFinalRoom = 0xD80C,

    /// <summary>Icarian City 1 (<c>DC02</c>).</summary>
    IcarianCity1 = 0xDC02,

    /// <summary>Icarian City 2 (inside wall) (<c>DC04</c>).</summary>
    IcarianCity2 = 0xDC04,

    /// <summary>Icarian City 3 (<c>DC08</c>).</summary>
    IcarianCity3 = 0xDC08,

    /// <summary>Icarian City 4 (<c>DC0C</c>).</summary>
    IcarianCity4 = 0xDC0C,

    /// <summary>Icarian City 5 (<c>DC10</c>).</summary>
    IcarianCity5 = 0xDC10,

    /// <summary>Ancient City (end) (<c>DC14</c>).</summary>
    AncientCityEnd = 0xDC14,

    /// <summary>Gaia 1 (<c>E004</c>).</summary>
    Gaia1 = 0xE004,

    /// <summary>Gaia 2 (<c>E006</c>).</summary>
    Gaia2 = 0xE006,

    /// <summary>Gaia 3 (<c>E008</c>).</summary>
    Gaia3 = 0xE008,

    /// <summary>Gaia 4 (<c>E010</c>).</summary>
    Gaia4 = 0xE010,

    /// <summary>Gaia 5 (<c>E018</c>).</summary>
    Gaia5 = 0xE018,

    /// <summary>Gaia core (<c>E01C</c>).</summary>
    GaiaCore = 0xE01C,

    /// <summary>Gaia final (<c>E020</c>).</summary>
    GaiaFinal = 0xE020,

    /// <summary>Spirits (ending) (<c>E400</c>).</summary>
    SpiritsEnding = 0xE400,

    /// <summary>Everyone chatting (ending) (<c>E404</c>).</summary>
    EveryoneChattingEnding = 0xE404,

    /// <summary>Zil Padon restored (ending) (<c>E408</c>).</summary>
    ZilPadonRestored = 0xE408,

    /// <summary>Petrified Forest restored (ending) (<c>E40C</c>).</summary>
    PetrifiedForestRestored = 0xE40C,

    /// <summary>Sue's narration (ending) (<c>E410</c>).</summary>
    SuesNarration = 0xE410,

    /// <summary>Port of Parm (ending) (<c>E414</c>).</summary>
    PortOfParmEnding = 0xE414,

    /// <summary>Ending Parm (<c>E418</c>).</summary>
    EndingParm = 0xE418,
}
