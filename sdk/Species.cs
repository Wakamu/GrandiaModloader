namespace Grandia.Sdk;

/// <summary>
/// M_DAT form-row / <c>monster_full_N</c> in TEXT/EN/strings.txt.
/// Same id <see cref="BattleLoadEvent.SetEnemies"/> uses as <c>species</c>.
/// </summary>
public enum Species
{
    None = 0,
    /// <summary>Gaia Ape (<c>0x01</c>).</summary>
    GaiaApe = 1,
    /// <summary>Gaia Horn (<c>0x02</c>).</summary>
    GaiaHorn = 2,
    /// <summary>Dragonoid (<c>0x03</c>).</summary>
    Dragonoid = 3,
    /// <summary>Gaia Drago (<c>0x04</c>).</summary>
    GaiaDrago = 4,
    /// <summary>Dragon Knight (<c>0x05</c>).</summary>
    DragonKnight = 5,
    /// <summary>Mountain Ape (<c>0x06</c>).</summary>
    MountainApe = 6,
    /// <summary>Yeti (<c>0x07</c>).</summary>
    Yeti = 7,
    /// <summary>Black Beret (<c>0x08</c>).</summary>
    BlackBeret = 8,
    /// <summary>Rock Man (<c>0x09</c>).</summary>
    RockMan = 9,
    /// <summary>Magma Man (<c>0x0A</c>).</summary>
    MagmaMan = 10,
    /// <summary>Iron Giant (<c>0x0B</c>).</summary>
    IronGiant = 11,
    /// <summary>Cactus Man (<c>0x0C</c>).</summary>
    CactusMan = 12,
    /// <summary>Vanatos (<c>0x0D</c>).</summary>
    Vanatos = 13,
    /// <summary>Gaia Man (<c>0x0E</c>).</summary>
    GaiaMan = 14,
    /// <summary>Medusa Dancer (<c>0x0F</c>).</summary>
    MedusaDancer = 15,
    /// <summary>Lilith (<c>0x10</c>).</summary>
    Lilith = 16,
    /// <summary>Naga Queen (<c>0x11</c>).</summary>
    NagaQueen = 17,
    /// <summary>Hyena Man (<c>0x12</c>).</summary>
    HyenaMan = 18,
    /// <summary>Wolfman (<c>0x13</c>).</summary>
    Wolfman = 19,
    /// <summary>Jackal (<c>0x14</c>).</summary>
    Jackal = 20,
    /// <summary>Red Devil (<c>0x15</c>).</summary>
    RedDevil = 21,
    /// <summary>Blue Devil (<c>0x16</c>).</summary>
    BlueDevil = 22,
    /// <summary>Nyalmot (<c>0x17</c>).</summary>
    Nyalmot = 23,
    /// <summary>Pink Mage (<c>0x18</c>).</summary>
    PinkMage = 24,
    /// <summary>Shaman (<c>0x19</c>).</summary>
    Shaman = 25,
    /// <summary>Layelah (<c>0x1A</c>).</summary>
    Layelah = 26,
    /// <summary>Pteranobone (<c>0x1B</c>).</summary>
    Pteranobone = 27,
    /// <summary>Bird Skull (<c>0x1C</c>).</summary>
    BirdSkull = 28,
    /// <summary>Skeleton (<c>0x1D</c>).</summary>
    Skeleton = 29,
    /// <summary>Zombie (<c>0x1E</c>).</summary>
    Zombie = 30,
    /// <summary>Lich (<c>0x1F</c>).</summary>
    Lich = 31,
    /// <summary>Gaia Zombie (<c>0x20</c>).</summary>
    GaiaZombie = 32,
    /// <summary>Warp Man (<c>0x21</c>).</summary>
    WarpMan = 33,
    /// <summary>Gaia Knight (<c>0x22</c>).</summary>
    GaiaKnight = 34,
    /// <summary>Spacetime Armor (<c>0x23</c>).</summary>
    SpacetimeArmor = 35,
    /// <summary>Ghostoid (<c>0x24</c>).</summary>
    Ghostoid = 36,
    /// <summary>Vengeful Spirit (<c>0x25</c>).</summary>
    VengefulSpirit = 37,
    /// <summary>Ghost (<c>0x26</c>).</summary>
    Ghost = 38,
    /// <summary>Lost Soul (<c>0x27</c>).</summary>
    LostSoul = 39,
    /// <summary>Critter (<c>0x28</c>).</summary>
    Critter = 40,
    /// <summary>Will-O'-Wisp (<c>0x29</c>).</summary>
    WillOWisp = 41,
    /// <summary>Hot Dog (<c>0x2A</c>).</summary>
    HotDog = 42,
    /// <summary>Fire Hound (<c>0x2B</c>).</summary>
    FireHound = 43,
    /// <summary>Cerberus (<c>0x2C</c>).</summary>
    Cerberus = 44,
    /// <summary>Snow Boar (<c>0x2D</c>).</summary>
    SnowBoar = 45,
    /// <summary>King Horn (<c>0x2E</c>).</summary>
    KingHorn = 46,
    /// <summary>Milda (<c>0x2F</c>).</summary>
    Milda = 47,
    /// <summary>Glug Bird (<c>0x30</c>).</summary>
    GlugBird = 48,
    /// <summary>Thud Bird (<c>0x31</c>).</summary>
    ThudBird = 49,
    /// <summary>Flap Bird (<c>0x32</c>).</summary>
    FlapBird = 50,
    /// <summary>Sphytaros (<c>0x33</c>).</summary>
    Sphytaros = 51,
    /// <summary>Sphynx (<c>0x34</c>).</summary>
    Sphynx = 52,
    /// <summary>Guardian (<c>0x35</c>).</summary>
    Guardian = 53,
    /// <summary>Land Slug (<c>0x36</c>).</summary>
    LandSlug = 54,
    /// <summary>Huge Pupa (<c>0x37</c>).</summary>
    HugePupa = 55,
    /// <summary>Gaia Slug (<c>0x38</c>).</summary>
    GaiaSlug = 56,
    /// <summary>Gaia Devil (<c>0x39</c>).</summary>
    GaiaDevil = 57,
    /// <summary>Toad Demon (<c>0x3A</c>).</summary>
    ToadDemon = 58,
    /// <summary>Satan (<c>0x3B</c>).</summary>
    Satan = 59,
    /// <summary>Baby Bat (<c>0x3C</c>).</summary>
    BabyBat = 60,
    /// <summary>Vampire Bat (<c>0x3D</c>).</summary>
    VampireBat = 61,
    /// <summary>Sonic Bat (<c>0x3E</c>).</summary>
    SonicBat = 62,
    /// <summary>Magic Head (<c>0x3F</c>).</summary>
    MagicHead = 63,
    /// <summary>Gaia Brain (<c>0x40</c>).</summary>
    GaiaBrain = 64,
    /// <summary>Brain Bat (<c>0x41</c>).</summary>
    BrainBat = 65,
    /// <summary>Clay Bird (<c>0x42</c>).</summary>
    ClayBird = 66,
    /// <summary>Rock Bird (<c>0x43</c>).</summary>
    RockBird = 67,
    /// <summary>Emerald Bird (<c>0x44</c>).</summary>
    EmeraldBird = 68,
    /// <summary>Sweet Moth (<c>0x45</c>).</summary>
    SweetMoth = 69,
    /// <summary>Dizzy Moth (<c>0x46</c>).</summary>
    DizzyMoth = 70,
    /// <summary>Giant Moth (<c>0x47</c>).</summary>
    GiantMoth = 71,
    /// <summary>Plop Mold (<c>0x48</c>).</summary>
    PlopMold = 72,
    /// <summary>Mold Bird (<c>0x49</c>).</summary>
    MoldBird = 73,
    /// <summary>Gaia Mold (<c>0x4A</c>).</summary>
    GaiaMold = 74,
    /// <summary>Marna Bug (<c>0x4B</c>).</summary>
    MarnaBug = 75,
    /// <summary>Beetlebug (<c>0x4C</c>).</summary>
    Beetlebug = 76,
    /// <summary>Metal Beetle (<c>0x4D</c>).</summary>
    MetalBeetle = 77,
    /// <summary>Zil Scorpion (<c>0x4E</c>).</summary>
    ZilScorpion = 78,
    /// <summary>Scissorlock (<c>0x4F</c>).</summary>
    Scissorlock = 79,
    /// <summary>Gaia Demon (<c>0x50</c>).</summary>
    GaiaDemon = 80,
    /// <summary>Crimsona (<c>0x51</c>).</summary>
    Crimsona = 81,
    /// <summary>Lesser Crab (<c>0x52</c>).</summary>
    LesserCrab = 82,
    /// <summary>Gaia Alien (<c>0x53</c>).</summary>
    GaiaAlien = 83,
    /// <summary>Spyder (<c>0x54</c>).</summary>
    Spyder = 84,
    /// <summary>Black Widow (<c>0x55</c>).</summary>
    BlackWidow = 85,
    /// <summary>Tarantula (<c>0x56</c>).</summary>
    Tarantula = 86,
    /// <summary>Ammonite (<c>0x57</c>).</summary>
    Ammonite = 87,
    /// <summary>Mad Snail (<c>0x58</c>).</summary>
    MadSnail = 88,
    /// <summary>Gaia Cyst (<c>0x59</c>).</summary>
    GaiaCyst = 89,
    /// <summary>Sea Jelly (<c>0x5A</c>).</summary>
    SeaJelly = 90,
    /// <summary>Mud Jelly (<c>0x5B</c>).</summary>
    MudJelly = 91,
    /// <summary>Skip (<c>0x5C</c>).</summary>
    Skip92 = 92,
    /// <summary>Hermit Crab (<c>0x5D</c>).</summary>
    HermitCrab = 93,
    /// <summary>Scarab (<c>0x5E</c>).</summary>
    Scarab = 94,
    /// <summary>Gaia Cancer (<c>0x5F</c>).</summary>
    GaiaCancer = 95,
    /// <summary>Sea Star (<c>0x60</c>).</summary>
    SeaStar = 96,
    /// <summary>Starfish (<c>0x61</c>).</summary>
    Starfish = 97,
    /// <summary>Gaia Star (<c>0x62</c>).</summary>
    GaiaStar = 98,
    /// <summary>Blue Kite (<c>0x63</c>).</summary>
    BlueKite = 99,
    /// <summary>Manta Ray (<c>0x64</c>).</summary>
    MantaRay = 100,
    /// <summary>Stingray (<c>0x65</c>).</summary>
    Stingray = 101,
    /// <summary>Sand Diver (<c>0x66</c>).</summary>
    SandDiver = 102,
    /// <summary>Sand Man (<c>0x67</c>).</summary>
    SandMan = 103,
    /// <summary>Gaia Scorpion (<c>0x68</c>).</summary>
    GaiaScorpion = 104,
    /// <summary>Giant Centipede (<c>0x69</c>).</summary>
    GiantCentipede = 105,
    /// <summary>Inchworm (<c>0x6A</c>).</summary>
    Inchworm = 106,
    /// <summary>Roadcrawler (<c>0x6B</c>).</summary>
    Roadcrawler = 107,
    /// <summary>Spitting Cobra (<c>0x6C</c>).</summary>
    SpittingCobra = 108,
    /// <summary>Pit Viper (<c>0x6D</c>).</summary>
    PitViper = 109,
    /// <summary>Skip (<c>0x6E</c>).</summary>
    Skip110 = 110,
    /// <summary>Sand Worm (<c>0x6F</c>).</summary>
    SandWorm = 111,
    /// <summary>Sand Snake (<c>0x70</c>).</summary>
    SandSnake = 112,
    /// <summary>Gaia Snake (<c>0x71</c>).</summary>
    GaiaSnake = 113,
    /// <summary>Chameleon (<c>0x72</c>).</summary>
    Chameleon = 114,
    /// <summary>Alligator (<c>0x73</c>).</summary>
    Alligator = 115,
    /// <summary>Salamadile (<c>0x74</c>).</summary>
    Salamadile = 116,
    /// <summary>Gill Newt (<c>0x75</c>).</summary>
    GillNewt = 117,
    /// <summary>Oopa-Loopa (<c>0x76</c>).</summary>
    OopaLoopa = 118,
    /// <summary>Gaia Fly (<c>0x77</c>).</summary>
    GaiaFly = 119,
    /// <summary>Horned Toad (<c>0x78</c>).</summary>
    HornedToad = 120,
    /// <summary>Mad Frog (<c>0x79</c>).</summary>
    MadFrog = 121,
    /// <summary>Toad King (<c>0x7A</c>).</summary>
    ToadKing = 122,
    /// <summary>Hippocamp (<c>0x7B</c>).</summary>
    Hippocamp = 123,
    /// <summary>Coelacanth (<c>0x7C</c>).</summary>
    Coelacanth = 124,
    /// <summary>Gaia Cactus (<c>0x7D</c>).</summary>
    GaiaCactus = 125,
    /// <summary>Green Slime (<c>0x7E</c>).</summary>
    GreenSlime = 126,
    /// <summary>Purple Slime (<c>0x7F</c>).</summary>
    PurpleSlime = 127,
    /// <summary>Red Slime (<c>0x80</c>).</summary>
    RedSlime = 128,
    /// <summary>Grim Haze (<c>0x81</c>).</summary>
    GrimHaze = 129,
    /// <summary>Gas Cloud (<c>0x82</c>).</summary>
    GasCloud = 130,
    /// <summary>Mist Wraith (<c>0x83</c>).</summary>
    MistWraith = 131,
    /// <summary>Odd Bird (<c>0x84</c>).</summary>
    OddBird = 132,
    /// <summary>Birdrake (<c>0x85</c>).</summary>
    Birdrake = 133,
    /// <summary>Dodo (<c>0x86</c>).</summary>
    Dodo = 134,
    /// <summary>Slipple (<c>0x87</c>).</summary>
    Slipple = 135,
    /// <summary>Gripple (<c>0x88</c>).</summary>
    Gripple = 136,
    /// <summary>Stuttle (<c>0x89</c>).</summary>
    Stuttle = 137,
    /// <summary>Ent (<c>0x8A</c>).</summary>
    Ent = 138,
    /// <summary>Mist Guard (<c>0x8B</c>).</summary>
    MistGuard = 139,
    /// <summary>Killer Tree (<c>0x8C</c>).</summary>
    KillerTree = 140,
    /// <summary>Private (<c>0x8D</c>).</summary>
    Private = 141,
    /// <summary>Sergeant (<c>0x8E</c>).</summary>
    Sergeant = 142,
    /// <summary>Elite Officer (<c>0x8F</c>).</summary>
    EliteOfficer = 143,
    /// <summary>Klepp Soldier (<c>0x90</c>).</summary>
    KleppSoldier = 144,
    /// <summary>Elite Klepp (<c>0x91</c>).</summary>
    EliteKlepp = 145,
    /// <summary>Klepp Knight (<c>0x92</c>).</summary>
    KleppKnight = 146,
    /// <summary>Lizard Rider (<c>0x93</c>).</summary>
    LizardRider = 147,
    /// <summary>Mad Rider (<c>0x94</c>).</summary>
    MadRider = 148,
    /// <summary>Klepp Rider (<c>0x95</c>).</summary>
    KleppRider = 149,
    /// <summary>Squid King (<c>0x96</c>).</summary>
    SquidKing = 150,
    /// <summary>Right Tentacle (<c>0x97</c>).</summary>
    RightTentacle151 = 151,
    /// <summary>Left Tentacle (<c>0x98</c>).</summary>
    LeftTentacle152 = 152,
    /// <summary>Kraken (<c>0x99</c>).</summary>
    Kraken = 153,
    /// <summary>Right Tentacle (<c>0x9A</c>).</summary>
    RightTentacle154 = 154,
    /// <summary>Left Tentacle (<c>0x9B</c>).</summary>
    LeftTentacle155 = 155,
    /// <summary>Ganymede (<c>0x9C</c>).</summary>
    Ganymede156 = 156,
    /// <summary>Ganymede (<c>0x9D</c>).</summary>
    Ganymede157 = 157,
    /// <summary>Skip (<c>0x9E</c>).</summary>
    Skip158 = 158,
    /// <summary>Mad Turtle (<c>0x9F</c>).</summary>
    MadTurtle159 = 159,
    /// <summary>Mad Turtle (<c>0xA0</c>).</summary>
    MadTurtle160 = 160,
    /// <summary>Gaia Bird (<c>0xA1</c>).</summary>
    GaiaBird = 161,
    /// <summary>Gargoyle (<c>0xA2</c>).</summary>
    Gargoyle162 = 162,
    /// <summary>Gargoyle (<c>0xA3</c>).</summary>
    Gargoyle163 = 163,
    /// <summary>Madragon (<c>0xA4</c>).</summary>
    Madragon164 = 164,
    /// <summary>Madragon (<c>0xA5</c>).</summary>
    Madragon165 = 165,
    /// <summary>Skip (<c>0xA6</c>).</summary>
    Skip166 = 166,
    /// <summary>Phantom Dragon (<c>0xA7</c>).</summary>
    PhantomDragon167 = 167,
    /// <summary>Phantom Dragon (<c>0xA8</c>).</summary>
    PhantomDragon168 = 168,
    /// <summary>Skip (<c>0xA9</c>).</summary>
    Skip169 = 169,
    /// <summary>Massacre Machine (<c>0xAA</c>).</summary>
    MassacreMachine170 = 170,
    /// <summary>Eye (<c>0xAB</c>).</summary>
    Eye171 = 171,
    /// <summary>Massacre Machine (<c>0xAC</c>).</summary>
    MassacreMachine172 = 172,
    /// <summary>Eye (<c>0xAD</c>).</summary>
    Eye173 = 173,
    /// <summary>Serpent (<c>0xAE</c>).</summary>
    Serpent = 174,
    /// <summary>Mean Head (<c>0xAF</c>).</summary>
    MeanHead = 175,
    /// <summary>Hot Head (<c>0xB0</c>).</summary>
    HotHead176 = 176,
    /// <summary>Nice Head (<c>0xB1</c>).</summary>
    NiceHead177 = 177,
    /// <summary>Bad Head (<c>0xB2</c>).</summary>
    BadHead = 178,
    /// <summary>Hydra (<c>0xB3</c>).</summary>
    Hydra = 179,
    /// <summary>Peril Head (<c>0xB4</c>).</summary>
    PerilHead = 180,
    /// <summary>Hot Head (<c>0xB5</c>).</summary>
    HotHead181 = 181,
    /// <summary>Nice Head (<c>0xB6</c>).</summary>
    NiceHead182 = 182,
    /// <summary>Awful Head (<c>0xB7</c>).</summary>
    AwfulHead = 183,
    /// <summary>Trent (<c>0xB8</c>).</summary>
    Trent = 184,
    /// <summary>Arm (<c>0xB9</c>).</summary>
    Arm185 = 185,
    /// <summary>Flower (<c>0xBA</c>).</summary>
    Flower186 = 186,
    /// <summary>Gaia Trent (<c>0xBB</c>).</summary>
    GaiaTrent = 187,
    /// <summary>Arm (<c>0xBC</c>).</summary>
    Arm188 = 188,
    /// <summary>Flower (<c>0xBD</c>).</summary>
    Flower189 = 189,
    /// <summary>Lord's Ghost (<c>0xBE</c>).</summary>
    LordsGhost190 = 190,
    /// <summary>Lord's Ghost (<c>0xBF</c>).</summary>
    LordsGhost191 = 191,
    /// <summary>Wand (<c>0xC0</c>).</summary>
    Wand192 = 192,
    /// <summary>Mage King (<c>0xC1</c>).</summary>
    MageKing193 = 193,
    /// <summary>Mage King (<c>0xC2</c>).</summary>
    MageKing194 = 194,
    /// <summary>Wand (<c>0xC3</c>).</summary>
    Wand195 = 195,
    /// <summary>Ruin Guard (<c>0xC4</c>).</summary>
    RuinGuard = 196,
    /// <summary>Ax (<c>0xC5</c>).</summary>
    Ax197 = 197,
    /// <summary>Skip (<c>0xC6</c>).</summary>
    Skip198 = 198,
    /// <summary>Boomerang (<c>0xC7</c>).</summary>
    Boomerang = 199,
    /// <summary>Great Susano-o (<c>0xC8</c>).</summary>
    GreatSusanoO = 200,
    /// <summary>Ax (<c>0xC9</c>).</summary>
    Ax201 = 201,
    /// <summary>Iron Ball (<c>0xCA</c>).</summary>
    IronBall = 202,
    /// <summary>Skip (<c>0xCB</c>).</summary>
    Skip203 = 203,
    /// <summary>Chang (<c>0xCC</c>).</summary>
    Chang = 204,
    /// <summary>Gadwin (<c>0xCD</c>).</summary>
    Gadwin205 = 205,
    /// <summary>Gadwin (<c>0xCE</c>).</summary>
    Gadwin206 = 206,
    /// <summary>Kung Fu Master (<c>0xCF</c>).</summary>
    KungFuMaster = 207,
    /// <summary>Mullen (<c>0xD0</c>).</summary>
    Mullen = 208,
    /// <summary>Saki (<c>0xD1</c>).</summary>
    Saki209 = 209,
    /// <summary>Saki (<c>0xD2</c>).</summary>
    Saki210 = 210,
    /// <summary>Mio (<c>0xD3</c>).</summary>
    Mio211 = 211,
    /// <summary>Mio (<c>0xD4</c>).</summary>
    Mio212 = 212,
    /// <summary>Nana (<c>0xD5</c>).</summary>
    Nana213 = 213,
    /// <summary>Nana (<c>0xD6</c>).</summary>
    Nana214 = 214,
    /// <summary>Grinwhale (<c>0xD7</c>).</summary>
    Grinwhale = 215,
    /// <summary>Lure (<c>0xD8</c>).</summary>
    Lure216 = 216,
    /// <summary>Slug Fish (<c>0xD9</c>).</summary>
    SlugFish = 217,
    /// <summary>Lure (<c>0xDA</c>).</summary>
    Lure218 = 218,
    /// <summary>Baal (<c>0xDB</c>).</summary>
    Baal219 = 219,
    /// <summary>Baal (<c>0xDC</c>).</summary>
    Baal220 = 220,
    /// <summary>Gaia Tentacle (<c>0xDD</c>).</summary>
    GaiaTentacle221 = 221,
    /// <summary>Baal (<c>0xDE</c>).</summary>
    Baal222 = 222,
    /// <summary>Gaia Tentacle (<c>0xDF</c>).</summary>
    GaiaTentacle223 = 223,
    /// <summary>Gaia Tentacle (<c>0xE0</c>).</summary>
    GaiaTentacle224 = 224,
    /// <summary>Gaia Battler (<c>0xE1</c>).</summary>
    GaiaBattler225 = 225,
    /// <summary>Right Hand (<c>0xE2</c>).</summary>
    RightHand226 = 226,
    /// <summary>Left Hand (<c>0xE3</c>).</summary>
    LeftHand227 = 227,
    /// <summary>Gaia Battler (<c>0xE4</c>).</summary>
    GaiaBattler228 = 228,
    /// <summary>Right Hand (<c>0xE5</c>).</summary>
    RightHand229 = 229,
    /// <summary>Left Hand (<c>0xE6</c>).</summary>
    LeftHand230 = 230,
    /// <summary>Gaia Battler (<c>0xE7</c>).</summary>
    GaiaBattler231 = 231,
    /// <summary>Right Hand (<c>0xE8</c>).</summary>
    RightHand232 = 232,
    /// <summary>Left Hand (<c>0xE9</c>).</summary>
    LeftHand233 = 233,
    /// <summary>Gaia Battler (<c>0xEA</c>).</summary>
    GaiaBattler234 = 234,
    /// <summary>Right Hand (<c>0xEB</c>).</summary>
    RightHand235 = 235,
    /// <summary>Left Hand (<c>0xEC</c>).</summary>
    LeftHand236 = 236,
    /// <summary>Gaia Core (<c>0xED</c>).</summary>
    GaiaCore237 = 237,
    /// <summary>Mega Gaia (<c>0xEE</c>).</summary>
    MegaGaia = 238,
    /// <summary>Giga Gaia (<c>0xEF</c>).</summary>
    GigaGaia = 239,
    /// <summary>Gaia Tentacle (<c>0xF0</c>).</summary>
    GaiaTentacle240 = 240,
    /// <summary>Evil Gaia (<c>0xF1</c>).</summary>
    EvilGaia = 241,
    /// <summary>Gaia Core (<c>0xF2</c>).</summary>
    GaiaCore242 = 242,
    /// <summary>Gaia Core (<c>0xF3</c>).</summary>
    GaiaCore243 = 243,
    /// <summary>Gaia Armor (<c>0xF4</c>).</summary>
    GaiaArmor = 244,
    /// <summary>Eye (<c>0xF5</c>).</summary>
    Eye245 = 245,
    /// <summary>Gaia Slime (<c>0xF6</c>).</summary>
    GaiaSlime = 246,
    /// <summary>Combatant (<c>0xF7</c>).</summary>
    Combatant = 247,
    /// <summary>Gaia Tree (<c>0xF8</c>).</summary>
    GaiaTree = 248,
    /// <summary>Gaia Beetle (<c>0xF9</c>).</summary>
    GaiaBeetle = 249,
    /// <summary>Leviathan (<c>0xFA</c>).</summary>
    Leviathan = 250,
    /// <summary>Right Tentacle (<c>0xFB</c>).</summary>
    RightTentacle251 = 251,
    /// <summary>Left Tentacle (<c>0xFC</c>).</summary>
    LeftTentacle252 = 252,
    /// <summary>Orc (<c>0xFD</c>).</summary>
    Orc = 253,
    /// <summary>Orc King (<c>0xFE</c>).</summary>
    OrcKing = 254,
    /// <summary>Dom Orc (<c>0xFF</c>).</summary>
    DomOrc = 255,
}
