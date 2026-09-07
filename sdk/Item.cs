namespace Grandia.Sdk;

/// <summary>
/// Vanilla item ids (WINDT / stash / enemy drops). Herbs is 346.
/// Same numbers <see cref="GameStash"/> and <see cref="EnemyDrop.Item"/> use.
/// </summary>
public enum Item
{
    None = 0,
    /// <summary>Life Jewel (<c>1</c>). Unused in vanilla; Redux fill.</summary>
    LifeJewel = 1,
    /// <summary>Mage Hat (<c>2</c>). Unused in vanilla; Redux fill.</summary>
    MageHat = 2,
    /// <summary>Yoyo (<c>3</c>). Unused in vanilla; Redux fill.</summary>
    Yoyo = 3,
    /// <summary>Basic Wand (<c>4</c>). Unused in vanilla; Redux fill.</summary>
    BasicWand = 4,
    /// <summary>ReDux Wand (<c>5</c>). Unused in vanilla; Redux fill.</summary>
    ReduxWand = 5,
    /// <summary>Lord's Wand (<c>6</c>). Unused in vanilla; Redux fill.</summary>
    LordsWand = 6,
    /// <summary>Magic Rope (<c>7</c>). Unused in vanilla; Redux fill.</summary>
    MagicRope = 7,
    /// <summary>Lure's Heart (<c>8</c>). Unused in vanilla; Redux fill.</summary>
    LuresHeart = 8,
    /// <summary>Enchanted Whip (<c>9</c>). Unused in vanilla; Redux fill.</summary>
    EnchantedWhip = 9,
    /// <summary>Spirit Stone (<c>10</c>).</summary>
    SpiritStone = 10,
    /// <summary>Hero's Armband (<c>11</c>).</summary>
    HerosArmband = 11,
    /// <summary>Horn of Knowledge (<c>12</c>).</summary>
    HornOfKnowledge = 12,
    /// <summary>Medal of Wisdom (<c>13</c>).</summary>
    MedalOfWisdom = 13,
    /// <summary>Medal of Knowledge (<c>14</c>).</summary>
    MedalOfKnowledge = 14,
    /// <summary>Gaia Sprout (<c>15</c>).</summary>
    GaiaSprout = 15,
    /// <summary>Gantz's Key (<c>16</c>).</summary>
    GantzsKey = 16,
    /// <summary>Intro Letter (<c>17</c>).</summary>
    IntroLetter = 17,
    /// <summary>Key to the Café (<c>18</c>).</summary>
    KeyToTheCaf = 18,
    /// <summary>Java's Wallet (<c>19</c>).</summary>
    JavasWallet = 19,
    /// <summary>Steamer Pass (<c>20</c>).</summary>
    SteamerPass = 20,
    /// <summary>Lilly's Letter (<c>21</c>).</summary>
    LillysLetter = 21,
    /// <summary>Cabin Key (<c>22</c>).</summary>
    CabinKey = 22,
    /// <summary>Sulfa Weed (<c>23</c>).</summary>
    SulfaWeed = 23,
    /// <summary>Jail Key (<c>24</c>).</summary>
    JailKey = 24,
    /// <summary>Master Key (<c>25</c>).</summary>
    MasterKey = 25,
    /// <summary>Nectar of the Gods (<c>26</c>).</summary>
    NectarOfTheGods = 26,
    /// <summary>Mist-Clearing Nut (<c>27</c>).</summary>
    MistClearingNut = 27,
    /// <summary>Sue's Shoes (<c>28</c>).</summary>
    SuesShoes = 28,
    /// <summary>Warrior's Spear (<c>29</c>).</summary>
    WarriorsSpear = 29,
    /// <summary>Teleportation Orb (<c>30</c>).</summary>
    TeleportationOrb = 30,
    /// <summary>Apron (<c>31</c>).</summary>
    Apron = 31,
    /// <summary>Pot Lid (<c>32</c>).</summary>
    PotLid = 32,
    /// <summary>Iron Pot (<c>33</c>).</summary>
    IronPot = 33,
    /// <summary>Wooden Sword (<c>34</c>).</summary>
    WoodenSword = 34,
    /// <summary>Spirit Sword (<c>35</c>).</summary>
    SpiritSword = 35,
    /// <summary>Biscuits (<c>36</c>).</summary>
    Biscuits = 36,
    /// <summary>Marie's Pin (<c>37</c>).</summary>
    MariesPin = 37,
    /// <summary>Coal Candy (<c>38</c>).</summary>
    CoalCandy = 38,
    /// <summary>Letter to Clara (<c>39</c>).</summary>
    LetterToClara = 39,
    /// <summary>Pearl (<c>40</c>).</summary>
    Pearl = 40,
    /// <summary>Rainbow Key (<c>41</c>).</summary>
    RainbowKey = 41,
    /// <summary>Boiled Egg (<c>42</c>).</summary>
    BoiledEgg = 42,
    /// <summary>Chocolate (<c>43</c>).</summary>
    Chocolate = 43,
    /// <summary>Jawbreaker (<c>44</c>).</summary>
    Jawbreaker = 44,
    /// <summary>Ring of Protection (<c>45</c>).</summary>
    RingOfProtection = 45,
    /// <summary>Frost Herb (<c>46</c>).</summary>
    FrostHerb = 46,
    /// <summary>Amulet of Relief (<c>47</c>).</summary>
    AmuletOfRelief = 47,
    /// <summary>Gaia Wand (<c>48</c>). Unused in vanilla; Redux fill.</summary>
    GaiaWand = 48,
    /// <summary>Agile Shoes (<c>49</c>). Unused in vanilla; Redux fill.</summary>
    AgileShoes = 49,
    /// <summary>Agile Hat (<c>50</c>). Unused in vanilla; Redux fill.</summary>
    AgileHat = 50,
    /// <summary>Camping Tent (<c>51</c>). Unused in vanilla; Redux fill.</summary>
    CampingTent = 51,
    /// <summary>Adventure Bow (<c>62</c>). Unused in vanilla; Redux fill.</summary>
    AdventureBow = 62,
    /// <summary>Knife of Judgment (<c>63</c>).</summary>
    KnifeOfJudgment = 63,
    /// <summary>Rusty Knife (<c>64</c>).</summary>
    RustyKnife = 64,
    /// <summary>Paring Knife (<c>65</c>).</summary>
    ParingKnife = 65,
    /// <summary>Hunter's Knife (<c>66</c>).</summary>
    HuntersKnife = 66,
    /// <summary>Flint Knife (<c>67</c>).</summary>
    FlintKnife = 67,
    /// <summary>Azure Knife (<c>68</c>).</summary>
    AzureKnife = 68,
    /// <summary>Shocking Knife (<c>69</c>).</summary>
    ShockingKnife = 69,
    /// <summary>Poisoned Knife (<c>70</c>).</summary>
    PoisonedKnife = 70,
    /// <summary>Assassin's Dagger (<c>71</c>).</summary>
    AssassinsDagger = 71,
    /// <summary>Bloody Knife (<c>72</c>).</summary>
    BloodyKnife = 72,
    /// <summary>Ice Pick (<c>73</c>).</summary>
    IcePick = 73,
    /// <summary>Force Knife (<c>74</c>).</summary>
    ForceKnife = 74,
    /// <summary>Godspeed Knife (<c>75</c>).</summary>
    GodspeedKnife = 75,
    /// <summary>Gust Knife (<c>76</c>).</summary>
    GustKnife = 76,
    /// <summary>Ruination Knife (<c>77</c>).</summary>
    RuinationKnife = 77,
    /// <summary>Thief Cutter (<c>78</c>).</summary>
    ThiefCutter = 78,
    /// <summary>Zero Knife (<c>79</c>).</summary>
    ZeroKnife = 79,
    /// <summary>Ceramic Sword (<c>80</c>).</summary>
    CeramicSword = 80,
    /// <summary>Admiral's Sword (<c>82</c>).</summary>
    AdmiralsSword = 82,
    /// <summary>Great Sword (<c>83</c>).</summary>
    GreatSword = 83,
    /// <summary>Army Saber (<c>84</c>).</summary>
    ArmySaber = 84,
    /// <summary>The Sword Himmler (<c>85</c>).</summary>
    TheSwordHimmler = 85,
    /// <summary>Angel's Darts (<c>86</c>).</summary>
    AngelsDarts = 86,
    /// <summary>Swordfish Sword (<c>87</c>).</summary>
    SwordfishSword = 87,
    /// <summary>Dragon Killer (<c>88</c>).</summary>
    DragonKiller = 88,
    /// <summary>Fire Sword (<c>89</c>).</summary>
    FireSword = 89,
    /// <summary>Shadow Sword (<c>90</c>).</summary>
    ShadowSword = 90,
    /// <summary>Gil Sword (<c>91</c>).</summary>
    GilSword = 91,
    /// <summary>Silence Sword (<c>92</c>).</summary>
    SilenceSword = 92,
    /// <summary>Wobbly Sword (<c>94</c>).</summary>
    WobblySword = 94,
    /// <summary>Main Gauche (<c>95</c>).</summary>
    MainGauche = 95,
    /// <summary>Holy Sword Lorenzo (<c>96</c>).</summary>
    HolySwordLorenzo = 96,
    /// <summary>Ice Blade (<c>97</c>).</summary>
    IceBlade = 97,
    /// <summary>Lightning Sword (<c>98</c>).</summary>
    LightningSword = 98,
    /// <summary>Battle Saber (<c>99</c>).</summary>
    BattleSaber = 99,
    /// <summary>Zero Sword (<c>100</c>).</summary>
    ZeroSword = 100,
    /// <summary>Wooden Pole (<c>101</c>).</summary>
    WoodenPole = 101,
    /// <summary>Metal Bat (<c>102</c>).</summary>
    MetalBat = 102,
    /// <summary>Zero Rod (<c>103</c>).</summary>
    ZeroRod = 103,
    /// <summary>Officer's Baton (<c>104</c>).</summary>
    OfficersBaton = 104,
    /// <summary>Magic Rod (<c>105</c>).</summary>
    MagicRod = 105,
    /// <summary>Miner's Hammer (<c>106</c>).</summary>
    MinersHammer = 106,
    /// <summary>Iron Mace (<c>107</c>).</summary>
    IronMace = 107,
    /// <summary>Army Darts (<c>108</c>).</summary>
    ArmyDarts = 108,
    /// <summary>Lassic Hammer (<c>109</c>).</summary>
    LassicHammer = 109,
    /// <summary>War Hammer (<c>110</c>).</summary>
    WarHammer = 110,
    /// <summary>Hertz Spike (<c>111</c>).</summary>
    HertzSpike = 111,
    /// <summary>Oracle's Staff (<c>112</c>).</summary>
    OraclesStaff = 112,
    /// <summary>Raincloud Staff (<c>113</c>).</summary>
    RaincloudStaff = 113,
    /// <summary>Staff of Life (<c>114</c>).</summary>
    StaffOfLife = 114,
    /// <summary>Warp Staff (<c>115</c>).</summary>
    WarpStaff = 115,
    /// <summary>Holy Mace (<c>116</c>).</summary>
    HolyMace = 116,
    /// <summary>Fire Rod (<c>117</c>).</summary>
    FireRod = 117,
    /// <summary>Aromatic Tree Root (<c>118</c>).</summary>
    AromaticTreeRoot = 118,
    /// <summary>Sparkling Rod (<c>119</c>).</summary>
    SparklingRod = 119,
    /// <summary>Spirit Staff (<c>120</c>).</summary>
    SpiritStaff = 120,
    /// <summary>Home Run Hammer (<c>121</c>).</summary>
    HomeRunHammer = 121,
    /// <summary>Rusty Shovel (<c>122</c>).</summary>
    RustyShovel = 122,
    /// <summary>Hand Ax (<c>123</c>).</summary>
    HandAx = 123,
    /// <summary>Ceremonial Rock Ax (<c>124</c>).</summary>
    CeremonialRockAx = 124,
    /// <summary>Big Hatchet (<c>125</c>).</summary>
    BigHatchet = 125,
    /// <summary>Woodchopper's Ax (<c>126</c>).</summary>
    WoodchoppersAx = 126,
    /// <summary>Dragon Bone Ax (<c>127</c>).</summary>
    DragonBoneAx = 127,
    /// <summary>Klepp's Sickle (<c>128</c>).</summary>
    KleppsSickle = 128,
    /// <summary>Frog Ax (<c>129</c>).</summary>
    FrogAx = 129,
    /// <summary>Bone Splitter Ax (<c>130</c>).</summary>
    BoneSplitterAx = 130,
    /// <summary>Earthen Ax (<c>131</c>).</summary>
    EarthenAx = 131,
    /// <summary>Buster Ax (<c>132</c>).</summary>
    BusterAx = 132,
    /// <summary>Wrecking Ax (<c>133</c>).</summary>
    WreckingAx = 133,
    /// <summary>Bent Mattock (<c>134</c>).</summary>
    BentMattock = 134,
    /// <summary>Zero Ax (<c>135</c>).</summary>
    ZeroAx = 135,
    /// <summary>Toy Bow and Arrow (<c>136</c>).</summary>
    ToyBowAndArrow = 136,
    /// <summary>Handmade Darts (<c>137</c>).</summary>
    HandmadeDarts = 137,
    /// <summary>Hunter's Bow (<c>138</c>).</summary>
    HuntersBow = 138,
    /// <summary>Flying Fish Bow (<c>139</c>).</summary>
    FlyingFishBow = 139,
    /// <summary>Hail Bow (<c>140</c>).</summary>
    HailBow = 140,
    /// <summary>Flint Bow (<c>141</c>).</summary>
    FlintBow = 141,
    /// <summary>Exorcising Bow (<c>142</c>).</summary>
    ExorcisingBow = 142,
    /// <summary>Cafu Shuriken (<c>143</c>).</summary>
    CafuShuriken = 143,
    /// <summary>Boomerang (<c>144</c>).</summary>
    Boomerang = 144,
    /// <summary>Fire Darts (<c>145</c>).</summary>
    FireDarts = 145,
    /// <summary>Evil Shuriken (<c>146</c>).</summary>
    EvilShuriken = 146,
    /// <summary>Demonslayer Boomer (<c>147</c>).</summary>
    DemonslayerBoomer = 147,
    /// <summary>Discus (<c>148</c>).</summary>
    Discus = 148,
    /// <summary>Cactus Thorns (<c>149</c>).</summary>
    CactusThorns = 149,
    /// <summary>Ice Boomerang (<c>150</c>).</summary>
    IceBoomerang = 150,
    /// <summary>Thunder Arrow (<c>151</c>).</summary>
    ThunderArrow = 151,
    /// <summary>Zero Shuriken (<c>152</c>).</summary>
    ZeroShuriken = 152,
    /// <summary>Zero Whip (<c>153</c>).</summary>
    ZeroWhip = 153,
    /// <summary>Mist-Cracking Whip (<c>154</c>).</summary>
    MistCrackingWhip = 154,
    /// <summary>Leather Whip (<c>155</c>).</summary>
    LeatherWhip = 155,
    /// <summary>Thorny Whip (<c>156</c>).</summary>
    ThornyWhip = 156,
    /// <summary>Catfish Whiskers (<c>157</c>).</summary>
    CatfishWhiskers = 157,
    /// <summary>Giant Snake Whip (<c>158</c>).</summary>
    GiantSnakeWhip = 158,
    /// <summary>Binding Whip (<c>159</c>).</summary>
    BindingWhip = 159,
    /// <summary>Morning Star (<c>160</c>).</summary>
    MorningStar = 160,
    /// <summary>Whip of Light (<c>161</c>).</summary>
    WhipOfLight = 161,
    /// <summary>Burning Hot Whip (<c>162</c>).</summary>
    BurningHotWhip = 162,
    /// <summary>Gale Whip (<c>163</c>).</summary>
    GaleWhip = 163,
    /// <summary>Adventure Clothes (<c>164</c>).</summary>
    AdventureClothes = 164,
    /// <summary>Sunday Best (<c>165</c>).</summary>
    SundayBest = 165,
    /// <summary>Sports Wear (<c>166</c>).</summary>
    SportsWear = 166,
    /// <summary>Work Clothes (<c>167</c>).</summary>
    WorkClothes = 167,
    /// <summary>Cactus Armor (<c>168</c>).</summary>
    CactusArmor = 168,
    /// <summary>Soldier's Uniform (<c>169</c>).</summary>
    SoldiersUniform = 169,
    /// <summary>Officer's Uniform (<c>170</c>).</summary>
    OfficersUniform = 170,
    /// <summary>Fairy Robe (<c>171</c>).</summary>
    FairyRobe = 171,
    /// <summary>Flying Dragon Vest (<c>172</c>).</summary>
    FlyingDragonVest = 172,
    /// <summary>Frog Shirt (<c>173</c>).</summary>
    FrogShirt = 173,
    /// <summary>Spy Clothes (<c>174</c>).</summary>
    SpyClothes = 174,
    /// <summary>Chain Mail (<c>175</c>).</summary>
    ChainMail = 175,
    /// <summary>Battle Bikini (<c>176</c>).</summary>
    BattleBikini = 176,
    /// <summary>Mogay Clothes (<c>177</c>).</summary>
    MogayClothes = 177,
    /// <summary>Mink Coat (<c>178</c>).</summary>
    MinkCoat = 178,
    /// <summary>Enchantress' Robe (<c>179</c>).</summary>
    EnchantressRobe = 179,
    /// <summary>Angel's Robe (<c>180</c>).</summary>
    AngelsRobe = 180,
    /// <summary>Robe of the Sun (<c>181</c>).</summary>
    RobeOfTheSun = 181,
    /// <summary>Breastplate (<c>182</c>).</summary>
    Breastplate = 182,
    /// <summary>Outdated Armor (<c>183</c>).</summary>
    OutdatedArmor = 183,
    /// <summary>Bamboo Armor (<c>184</c>).</summary>
    BambooArmor = 184,
    /// <summary>Shell Armor (<c>185</c>).</summary>
    ShellArmor = 185,
    /// <summary>Thick Armor (<c>186</c>).</summary>
    ThickArmor = 186,
    /// <summary>Swordfish Armor (<c>187</c>).</summary>
    SwordfishArmor = 187,
    /// <summary>Skull Armor (<c>188</c>).</summary>
    SkullArmor = 188,
    /// <summary>Chameleon Armor (<c>189</c>).</summary>
    ChameleonArmor = 189,
    /// <summary>Plug Suit (<c>190</c>).</summary>
    PlugSuit = 190,
    /// <summary>Aura Armor (<c>191</c>).</summary>
    AuraArmor = 191,
    /// <summary>Dark Armor (<c>192</c>).</summary>
    DarkArmor = 192,
    /// <summary>Warrior's Mail (<c>193</c>).</summary>
    WarriorsMail = 193,
    /// <summary>Spirit Armor (<c>194</c>).</summary>
    SpiritArmor = 194,
    /// <summary>Devil's Robe (<c>195</c>).</summary>
    DevilsRobe = 195,
    /// <summary>Magic Robe (<c>196</c>).</summary>
    MagicRobe = 196,
    /// <summary>Cutting Board (<c>197</c>).</summary>
    CuttingBoard = 197,
    /// <summary>Woolen Mittens (<c>198</c>).</summary>
    WoolenMittens = 198,
    /// <summary>Leather Gloves (<c>199</c>).</summary>
    LeatherGloves = 199,
    /// <summary>Escargot Shield (<c>200</c>).</summary>
    EscargotShield = 200,
    /// <summary>Oaken Shield (<c>201</c>).</summary>
    OakenShield = 201,
    /// <summary>Shell Shield (<c>202</c>).</summary>
    ShellShield = 202,
    /// <summary>Seashell Shield (<c>203</c>).</summary>
    SeashellShield = 203,
    /// <summary>Mushroom Shield (<c>204</c>).</summary>
    MushroomShield = 204,
    /// <summary>MagicMirror Shield (<c>205</c>).</summary>
    MagicMirrorShield = 205,
    /// <summary>Alligator Gauntlet (<c>206</c>).</summary>
    AlligatorGauntlet = 206,
    /// <summary>Leaf Shield (<c>207</c>).</summary>
    LeafShield = 207,
    /// <summary>Lafa Flower Shield (<c>208</c>).</summary>
    LafaFlowerShield = 208,
    /// <summary>Power Shield (<c>209</c>).</summary>
    PowerShield = 209,
    /// <summary>Moonlight Shield (<c>210</c>).</summary>
    MoonlightShield = 210,
    /// <summary>Gauntlets (<c>211</c>).</summary>
    Gauntlets = 211,
    /// <summary>Dragon Gauntlet (<c>212</c>).</summary>
    DragonGauntlet = 212,
    /// <summary>Heavy Shield (<c>213</c>).</summary>
    HeavyShield = 213,
    /// <summary>Gauntlets of Light (<c>214</c>).</summary>
    GauntletsOfLight = 214,
    /// <summary>Spirit Shield (<c>215</c>).</summary>
    SpiritShield = 215,
    /// <summary>Magic Gloves (<c>216</c>).</summary>
    MagicGloves = 216,
    /// <summary>Zero Shield (<c>217</c>).</summary>
    ZeroShield = 217,
    /// <summary>Goggles (<c>218</c>).</summary>
    Goggles = 218,
    /// <summary>Ribbon (<c>219</c>).</summary>
    Ribbon = 219,
    /// <summary>Fluffy Ribbon (<c>220</c>).</summary>
    FluffyRibbon = 220,
    /// <summary>Barrette (<c>221</c>).</summary>
    Barrette = 221,
    /// <summary>Pirate's Hat (<c>222</c>).</summary>
    PiratesHat = 222,
    /// <summary>Cowboy Hat (<c>223</c>).</summary>
    CowboyHat = 223,
    /// <summary>Climbing Hat (<c>224</c>).</summary>
    ClimbingHat = 224,
    /// <summary>Odd Hat (<c>225</c>).</summary>
    OddHat = 225,
    /// <summary>Headgear (<c>226</c>).</summary>
    Headgear = 226,
    /// <summary>Iron Bandana (<c>227</c>).</summary>
    IronBandana = 227,
    /// <summary>Feathered Turban (<c>228</c>).</summary>
    FeatheredTurban = 228,
    /// <summary>Angel's Hat (<c>229</c>).</summary>
    AngelsHat = 229,
    /// <summary>Pope's Hat (<c>230</c>).</summary>
    PopesHat = 230,
    /// <summary>Safety Helmet (<c>231</c>).</summary>
    SafetyHelmet = 231,
    /// <summary>Antler Helmet (<c>232</c>).</summary>
    AntlerHelmet = 232,
    /// <summary>Pearl Helmet (<c>233</c>).</summary>
    PearlHelmet = 233,
    /// <summary>Pirate's Helmet (<c>234</c>).</summary>
    PiratesHelmet = 234,
    /// <summary>Stone Head (<c>235</c>).</summary>
    StoneHead = 235,
    /// <summary>Swallowtail Hat (<c>236</c>).</summary>
    SwallowtailHat = 236,
    /// <summary>Mystic Mask (<c>237</c>).</summary>
    MysticMask = 237,
    /// <summary>Death Mask (<c>238</c>).</summary>
    DeathMask = 238,
    /// <summary>Ogre Helm (<c>239</c>).</summary>
    OgreHelm = 239,
    /// <summary>Charisma Helm (<c>240</c>).</summary>
    CharismaHelm = 240,
    /// <summary>Spirit Helmet (<c>241</c>).</summary>
    SpiritHelmet = 241,
    /// <summary>Holy Crown (<c>242</c>).</summary>
    HolyCrown = 242,
    /// <summary>Cactus Helm (<c>243</c>).</summary>
    CactusHelm = 243,
    /// <summary>Fairy Tiara (<c>244</c>).</summary>
    FairyTiara = 244,
    /// <summary>Man's Headband (<c>245</c>).</summary>
    MansHeadband = 245,
    /// <summary>Battle Helm (<c>246</c>).</summary>
    BattleHelm = 246,
    /// <summary>Sneakers (<c>247</c>).</summary>
    Sneakers = 247,
    /// <summary>Dress Shoes (<c>248</c>).</summary>
    DressShoes = 248,
    /// <summary>Air Sneakers (<c>249</c>).</summary>
    AirSneakers = 249,
    /// <summary>Shiny Shoes (<c>250</c>).</summary>
    ShinyShoes = 250,
    /// <summary>Rubber Boots (<c>251</c>).</summary>
    RubberBoots = 251,
    /// <summary>Leather Greaves (<c>252</c>).</summary>
    LeatherGreaves = 252,
    /// <summary>Hunter's Boots (<c>253</c>).</summary>
    HuntersBoots = 253,
    /// <summary>Army Boots (<c>254</c>).</summary>
    ArmyBoots = 254,
    /// <summary>Curious Clogs (<c>255</c>).</summary>
    CuriousClogs = 255,
    /// <summary>Dragon Boots (<c>256</c>).</summary>
    DragonBoots = 256,
    /// <summary>Ninja Sandals (<c>257</c>).</summary>
    NinjaSandals = 257,
    /// <summary>Winged Boots (<c>258</c>).</summary>
    WingedBoots = 258,
    /// <summary>Beach Sandals (<c>259</c>).</summary>
    BeachSandals = 259,
    /// <summary>Mach 1 Boots (<c>260</c>).</summary>
    Mach1Boots = 260,
    /// <summary>Heavy Boots (<c>261</c>).</summary>
    HeavyBoots = 261,
    /// <summary>Queen Heels (<c>262</c>).</summary>
    QueenHeels = 262,
    /// <summary>Iron Clogs (<c>263</c>).</summary>
    IronClogs = 263,
    /// <summary>Ogre Boots (<c>264</c>).</summary>
    OgreBoots = 264,
    /// <summary>Rabbit Shoes (<c>265</c>).</summary>
    RabbitShoes = 265,
    /// <summary>Rainbow High Heels (<c>266</c>).</summary>
    RainbowHighHeels = 266,
    /// <summary>Wolf Boots (<c>267</c>).</summary>
    WolfBoots = 267,
    /// <summary>Lion Boots (<c>268</c>).</summary>
    LionBoots = 268,
    /// <summary>Battle Boots (<c>269</c>).</summary>
    BattleBoots = 269,
    /// <summary>Spirit Shoes (<c>270</c>).</summary>
    SpiritShoes = 270,
    /// <summary>Glass Slippers (<c>271</c>).</summary>
    GlassSlippers = 271,
    /// <summary>Warp Shoes (<c>272</c>).</summary>
    WarpShoes = 272,
    /// <summary>Crampons (<c>273</c>).</summary>
    Crampons = 273,
    /// <summary>Zero Boots (<c>274</c>).</summary>
    ZeroBoots = 274,
    /// <summary>Diana's Amulet (<c>276</c>).</summary>
    DianasAmulet = 276,
    /// <summary>Hero's Badge (<c>277</c>).</summary>
    HerosBadge = 277,
    /// <summary>Demon Sword Amulet (<c>278</c>).</summary>
    DemonSwordAmulet = 278,
    /// <summary>Officer's Badge (<c>279</c>).</summary>
    OfficersBadge = 279,
    /// <summary>Black Belt (<c>280</c>).</summary>
    BlackBelt = 280,
    /// <summary>Chain Earrings (<c>281</c>).</summary>
    ChainEarrings = 281,
    /// <summary>Titan's Ring (<c>282</c>).</summary>
    TitansRing = 282,
    /// <summary>Fireproof Cape (<c>283</c>).</summary>
    FireproofCape = 283,
    /// <summary>Fire Charm (<c>284</c>).</summary>
    FireCharm = 284,
    /// <summary>Water Charm (<c>285</c>).</summary>
    WaterCharm = 285,
    /// <summary>Wind Charm (<c>286</c>).</summary>
    WindCharm = 286,
    /// <summary>Earth Charm (<c>287</c>).</summary>
    EarthCharm = 287,
    /// <summary>Counter Ring (<c>288</c>).</summary>
    CounterRing = 288,
    /// <summary>Secret Move Ring (<c>289</c>).</summary>
    SecretMoveRing = 289,
    /// <summary>Hurricane Belt (<c>290</c>).</summary>
    HurricaneBelt = 290,
    /// <summary>Mama's Amulet (<c>291</c>).</summary>
    MamasAmulet = 291,
    /// <summary>Jade Charm (<c>292</c>).</summary>
    JadeCharm = 292,
    /// <summary>Tree God Amulet (<c>293</c>).</summary>
    TreeGodAmulet = 293,
    /// <summary>Light God Amulet (<c>294</c>).</summary>
    LightGodAmulet = 294,
    /// <summary>Ancestor's Amulet (<c>295</c>).</summary>
    AncestorsAmulet = 295,
    /// <summary>Raincoat (<c>296</c>).</summary>
    Raincoat = 296,
    /// <summary>Iridescent Amulet (<c>297</c>).</summary>
    IridescentAmulet = 297,
    /// <summary>Move Unblocker (<c>298</c>).</summary>
    MoveUnblocker = 298,
    /// <summary>Medal of Yore (<c>299</c>).</summary>
    MedalOfYore = 299,
    /// <summary>Spirit Charm (<c>300</c>).</summary>
    SpiritCharm = 300,
    /// <summary>Phantom Silk (<c>301</c>).</summary>
    PhantomSilk = 301,
    /// <summary>Lightning Charm (<c>302</c>).</summary>
    LightningCharm = 302,
    /// <summary>Forest Charm (<c>303</c>).</summary>
    ForestCharm = 303,
    /// <summary>Explosion Charm (<c>304</c>).</summary>
    ExplosionCharm = 304,
    /// <summary>Blizzard Charm (<c>305</c>).</summary>
    BlizzardCharm = 305,
    /// <summary>Wind Belt (<c>306</c>).</summary>
    WindBelt = 306,
    /// <summary>Confusion Charm (<c>307</c>).</summary>
    ConfusionCharm = 307,
    /// <summary>Paralysis Charm (<c>308</c>).</summary>
    ParalysisCharm = 308,
    /// <summary>Magic Block Charm (<c>309</c>).</summary>
    MagicBlockCharm = 309,
    /// <summary>Sudden Death Charm (<c>310</c>).</summary>
    SuddenDeathCharm = 310,
    /// <summary>Poison Charm (<c>311</c>).</summary>
    PoisonCharm = 311,
    /// <summary>Talisman (<c>312</c>).</summary>
    Talisman = 312,
    /// <summary>Sonic Belt (<c>313</c>).</summary>
    SonicBelt = 313,
    /// <summary>Metal Frog (<c>314</c>).</summary>
    MetalFrog = 314,
    /// <summary>Revival Stone (<c>315</c>).</summary>
    RevivalStone = 315,
    /// <summary>Scarab (<c>317</c>).</summary>
    Scarab = 317,
    /// <summary>Demon Eye Stone (<c>318</c>).</summary>
    DemonEyeStone = 318,
    /// <summary>Jewel of Life (<c>319</c>).</summary>
    JewelOfLife = 319,
    /// <summary>Ankh of Temptation (<c>320</c>).</summary>
    AnkhOfTemptation = 320,
    /// <summary>Anklet (<c>321</c>).</summary>
    Anklet = 321,
    /// <summary>Energy Ring (<c>322</c>).</summary>
    EnergyRing = 322,
    /// <summary>Disease Charm (<c>323</c>).</summary>
    DiseaseCharm = 323,
    /// <summary>Paperweight (<c>324</c>).</summary>
    Paperweight = 324,
    /// <summary>Combat Anklet (<c>325</c>).</summary>
    CombatAnklet = 325,
    /// <summary>Chain of Gems (<c>326</c>).</summary>
    ChainOfGems = 326,
    /// <summary>Satisfaction Gem (<c>327</c>).</summary>
    SatisfactionGem = 327,
    /// <summary>Soul of Asura (<c>328</c>).</summary>
    SoulOfAsura = 328,
    /// <summary>Crescent Jade (<c>329</c>).</summary>
    CrescentJade = 329,
    /// <summary>Dragon Scales (<c>330</c>).</summary>
    DragonScales = 330,
    /// <summary>Spectacles (<c>331</c>).</summary>
    Spectacles = 331,
    /// <summary>Rune Ring (<c>332</c>).</summary>
    RuneRing = 332,
    /// <summary>Tear Jewel (<c>333</c>).</summary>
    TearJewel = 333,
    /// <summary>Spirit Potion (<c>334</c>).</summary>
    SpiritPotion = 334,
    /// <summary>Would Salve (<c>335</c>).</summary>
    WouldSalve = 335,
    /// <summary>Baobab Fruit (<c>336</c>).</summary>
    BaobabFruit = 336,
    /// <summary>Boiled Coconut (<c>337</c>).</summary>
    BoiledCoconut = 337,
    /// <summary>Chocolate Cookies (<c>338</c>).</summary>
    ChocolateCookies = 338,
    /// <summary>Honey (<c>339</c>).</summary>
    Honey = 339,
    /// <summary>Ultra Drink (<c>340</c>).</summary>
    UltraDrink = 340,
    /// <summary>Weeds (<c>341</c>).</summary>
    Weeds = 341,
    /// <summary>Dried Fish (<c>342</c>).</summary>
    DriedFish = 342,
    /// <summary>Bamboo Shoots (<c>343</c>).</summary>
    BambooShoots = 343,
    /// <summary>Beef Jerky (<c>344</c>).</summary>
    BeefJerky = 344,
    /// <summary>Box Lunch (<c>345</c>).</summary>
    BoxLunch = 345,
    /// <summary>Herbs (<c>346</c>).</summary>
    Herbs = 346,
    /// <summary>White Sulfa Weed (<c>347</c>).</summary>
    WhiteSulfaWeed = 347,
    /// <summary>Smarna Weed (<c>348</c>).</summary>
    SmarnaWeed = 348,
    /// <summary>Cholla Flowers (<c>349</c>).</summary>
    ChollaFlowers = 349,
    /// <summary>Bamo Fruit (<c>350</c>).</summary>
    BamoFruit = 350,
    /// <summary>Squid Guts (<c>351</c>).</summary>
    SquidGuts = 351,
    /// <summary>Move Mushroom (<c>352</c>).</summary>
    MoveMushroom = 352,
    /// <summary>Power Mushroom (<c>353</c>).</summary>
    PowerMushroom = 353,
    /// <summary>Poison Antidote (<c>354</c>).</summary>
    PoisonAntidote = 354,
    /// <summary>Ginseng (<c>355</c>).</summary>
    Ginseng = 355,
    /// <summary>Banana (<c>356</c>).</summary>
    Banana = 356,
    /// <summary>Bandage (<c>357</c>).</summary>
    Bandage = 357,
    /// <summary>Box of Sweets (<c>358</c>).</summary>
    BoxOfSweets = 358,
    /// <summary>First Aid Kit (<c>359</c>).</summary>
    FirstAidKit = 359,
    /// <summary>Red Medicine (<c>360</c>).</summary>
    RedMedicine = 360,
    /// <summary>Blue Medicine (<c>361</c>).</summary>
    BlueMedicine = 361,
    /// <summary>Yellow Medicine (<c>362</c>).</summary>
    YellowMedicine = 362,
    /// <summary>Crimson Potion (<c>363</c>).</summary>
    CrimsonPotion = 363,
    /// <summary>Deep Blue Potion (<c>364</c>).</summary>
    DeepBluePotion = 364,
    /// <summary>Golden Potion (<c>365</c>).</summary>
    GoldenPotion = 365,
    /// <summary>Magic Lamp (<c>366</c>).</summary>
    MagicLamp = 366,
    /// <summary>Poison Antidote (<c>367</c>).</summary>
    PoisonAntidote367 = 367,
    /// <summary>Vaccine (<c>368</c>).</summary>
    Vaccine = 368,
    /// <summary>Eye Drops (<c>369</c>).</summary>
    EyeDrops = 369,
    /// <summary>Smelling Salts (<c>370</c>).</summary>
    SmellingSalts = 370,
    /// <summary>Paralysis Ointment (<c>371</c>).</summary>
    ParalysisOintment = 371,
    /// <summary>Spell Breaker (<c>372</c>).</summary>
    SpellBreaker = 372,
    /// <summary>Move Breaker (<c>373</c>).</summary>
    MoveBreaker = 373,
    /// <summary>Resurrect Potion (<c>374</c>).</summary>
    ResurrectPotion = 374,
    /// <summary>Panacea (<c>375</c>).</summary>
    Panacea = 375,
    /// <summary>Bond of Trust (<c>376</c>).</summary>
    BondOfTrust = 376,
    /// <summary>Seed of Power (<c>377</c>).</summary>
    SeedOfPower = 377,
    /// <summary>Seed of Defense (<c>378</c>).</summary>
    SeedOfDefense = 378,
    /// <summary>Seed of Speed (<c>379</c>).</summary>
    SeedOfSpeed = 379,
    /// <summary>Seed of Running (<c>380</c>).</summary>
    SeedOfRunning = 380,
    /// <summary>All-Around Seed (<c>381</c>).</summary>
    AllAroundSeed = 381,
    /// <summary>Seed of Life (<c>382</c>).</summary>
    SeedOfLife = 382,
    /// <summary>Seed of Magic (<c>383</c>).</summary>
    SeedOfMagic = 383,
    /// <summary>Seed of Moves (<c>384</c>).</summary>
    SeedOfMoves = 384,
    /// <summary>Mace Coloring Book (<c>385</c>).</summary>
    MaceColoringBook = 385,
    /// <summary>Bow Coloring Book (<c>386</c>).</summary>
    BowColoringBook = 386,
    /// <summary>Sword Secrets (<c>387</c>).</summary>
    SwordSecrets = 387,
    /// <summary>Fire Secrets (<c>388</c>).</summary>
    FireSecrets = 388,
    /// <summary>Earth Secrets (<c>389</c>).</summary>
    EarthSecrets = 389,
    /// <summary>How to Cut 'Em (<c>390</c>).</summary>
    HowToCutEm = 390,
    /// <summary>How to Pound 'Em (<c>391</c>).</summary>
    HowToPoundEm = 391,
    /// <summary>How to Chop 'Em (<c>392</c>).</summary>
    HowToChopEm = 392,
    /// <summary>Roach Bomb (<c>393</c>).</summary>
    RoachBomb = 393,
    /// <summary>Firewood Sparks (<c>394</c>).</summary>
    FirewoodSparks = 394,
    /// <summary>Mana Egg (<c>395</c>).</summary>
    ManaEgg = 395,
    /// <summary>Holy Fire (<c>396</c>).</summary>
    HolyFire = 396,
    /// <summary>Hand Grenade (<c>397</c>).</summary>
    HandGrenade = 397,
    /// <summary>Dynamite (<c>398</c>).</summary>
    Dynamite = 398,
    /// <summary>Rocket Fireworks (<c>399</c>).</summary>
    RocketFireworks = 399,
    /// <summary>Launch Fireworks (<c>400</c>).</summary>
    LaunchFireworks = 400,
    /// <summary>BOOM! Scroll (<c>401</c>).</summary>
    BOOMScroll = 401,
    /// <summary>Howler Scroll (<c>402</c>).</summary>
    HowlerScroll = 402,
    /// <summary>Vacuum Scroll (<c>403</c>).</summary>
    VacuumScroll = 403,
    /// <summary>Tremor Scroll (<c>404</c>).</summary>
    TremorScroll = 404,
    /// <summary>Zap! Book (<c>405</c>).</summary>
    ZapBook = 405,
    /// <summary>Lightning Scroll (<c>406</c>).</summary>
    LightningScroll = 406,
    /// <summary>Gale Scroll (<c>407</c>).</summary>
    GaleScroll = 407,
    /// <summary>Overflowing Walnut (<c>408</c>).</summary>
    OverflowingWalnut = 408,
    /// <summary>Restraint Walnut (<c>409</c>).</summary>
    RestraintWalnut = 409,
    /// <summary>Sonic Walnut (<c>410</c>).</summary>
    SonicWalnut = 410,
    /// <summary>Running Walnut (<c>411</c>).</summary>
    RunningWalnut = 411,
    /// <summary>Snooze Scroll (<c>412</c>).</summary>
    SnoozeScroll = 412,
    /// <summary>Poisoned Apple (<c>413</c>).</summary>
    PoisonedApple = 413,
    /// <summary>Culture Medium (<c>414</c>).</summary>
    CultureMedium = 414,
    /// <summary>Dream Truffle (<c>415</c>).</summary>
    DreamTruffle = 415,
    /// <summary>Paralyze Mushroom (<c>416</c>).</summary>
    ParalyzeMushroom = 416,
    /// <summary>Orb of Silence (<c>417</c>).</summary>
    OrbOfSilence = 417,
    /// <summary>Black Onyx (<c>418</c>).</summary>
    BlackOnyx = 418,
    /// <summary>Weak-Knee Weed (<c>419</c>).</summary>
    WeakKneeWeed = 419,
    /// <summary>Ragged Weed (<c>420</c>).</summary>
    RaggedWeed = 420,
    /// <summary>Slouch Weed (<c>421</c>).</summary>
    SlouchWeed = 421,
    /// <summary>Trudge Weed (<c>422</c>).</summary>
    TrudgeWeed = 422,
    /// <summary>Root of Confusion (<c>423</c>).</summary>
    RootOfConfusion = 423,
    /// <summary>Torte's Whistle (<c>424</c>).</summary>
    TortesWhistle = 424,
    /// <summary>Freesia Flowers (<c>425</c>).</summary>
    FreesiaFlowers = 425,
    /// <summary>Cone of Light (<c>426</c>).</summary>
    ConeOfLight = 426,
    /// <summary>Mikeroma Scroll (<c>427</c>).</summary>
    MikeromaScroll = 427,
    /// <summary>Shuffle Card (<c>428</c>).</summary>
    ShuffleCard = 428,
    /// <summary>Miracle Drink (<c>429</c>).</summary>
    MiracleDrink = 429,
    /// <summary>Gold Key (<c>430</c>).</summary>
    GoldKey = 430,
    /// <summary>Silver Key (<c>431</c>).</summary>
    SilverKey = 431,
    /// <summary>Key of Temptation (<c>432</c>).</summary>
    KeyOfTemptation = 432,
    /// <summary>Soldier's Key (<c>433</c>).</summary>
    SoldiersKey = 433,
    /// <summary>Pretty Jewel (<c>434</c>).</summary>
    PrettyJewel = 434,
    /// <summary>Pretty Jewel (<c>435</c>).</summary>
    PrettyJewel435 = 435,
    /// <summary>Pretty Jewel (<c>436</c>).</summary>
    PrettyJewel436 = 436,
    /// <summary>Horn of Inogon (<c>437</c>).</summary>
    HornOfInogon = 437,
    /// <summary>Key (<c>438</c>).</summary>
    Key = 438,
    /// <summary>Health Weed (<c>439</c>).</summary>
    HealthWeed = 439,
    /// <summary>Snake Earrings (<c>440</c>).</summary>
    SnakeEarrings = 440,
    /// <summary>Resurrect Potion (<c>441</c>).</summary>
    ResurrectPotion441 = 441,
    /// <summary>Expensive Jewel (<c>442</c>).</summary>
    ExpensiveJewel = 442,
    /// <summary>Rainbow Weed (<c>443</c>).</summary>
    RainbowWeed = 443,
    /// <summary>Smoked Salmon (<c>444</c>).</summary>
    SmokedSalmon = 444,
    /// <summary>Prime Rib (<c>445</c>).</summary>
    PrimeRib = 445,
    /// <summary>Rescue Set (<c>446</c>).</summary>
    RescueSet = 446,
    /// <summary>Black Nail Polish (<c>447</c>).</summary>
    BlackNailPolish = 447,
    /// <summary>Spirit Whip (<c>448</c>). Unused in vanilla; Redux fill.</summary>
    SpiritWhip = 448,
    /// <summary>Thor's Fury (<c>449</c>).</summary>
    ThorsFury = 449,
    /// <summary>Magic Lipstick (<c>450</c>).</summary>
    MagicLipstick = 450,
    /// <summary>Elite Badge (<c>451</c>).</summary>
    EliteBadge = 451,
    /// <summary>Fruit of Power (<c>452</c>).</summary>
    FruitOfPower = 452,
    /// <summary>Fruit of Defense (<c>453</c>).</summary>
    FruitOfDefense = 453,
    /// <summary>Fruit of Speed (<c>454</c>).</summary>
    FruitOfSpeed = 454,
    /// <summary>Fruit of Agility (<c>455</c>).</summary>
    FruitOfAgility = 455,
    /// <summary>All-Around Fruit (<c>456</c>).</summary>
    AllAroundFruit = 456,
    /// <summary>Fruit of Life (<c>457</c>).</summary>
    FruitOfLife = 457,
    /// <summary>Fruit of Magic (<c>458</c>).</summary>
    FruitOfMagic = 458,
    /// <summary>Fruit of Moves (<c>459</c>).</summary>
    FruitOfMoves = 459,
    /// <summary>Mogay Teachings 1 (<c>460</c>).</summary>
    MogayTeachings1 = 460,
    /// <summary>Mogay Teachings 2 (<c>461</c>).</summary>
    MogayTeachings2 = 461,
    /// <summary>Mogay Teachings 3 (<c>462</c>).</summary>
    MogayTeachings3 = 462,
    /// <summary>Ring of Rage (<c>463</c>).</summary>
    RingOfRage = 463,
    /// <summary>Holy Ring (<c>464</c>).</summary>
    HolyRing = 464,
    /// <summary>Mysterious Veil (<c>465</c>).</summary>
    MysteriousVeil = 465,
    /// <summary>Blizzard Scroll (<c>466</c>).</summary>
    BlizzardScroll = 466,
    /// <summary>Telescope (<c>467</c>).</summary>
    Telescope = 467,
    /// <summary>Energy Charm (<c>468</c>).</summary>
    EnergyCharm = 468,
    /// <summary>Devil's Anklet (<c>469</c>).</summary>
    DevilsAnklet = 469,
    /// <summary>Astral Miracle (<c>471</c>).</summary>
    AstralMiracle = 471,
    /// <summary>Ethereal Miracle (<c>472</c>).</summary>
    EtherealMiracle = 472,
    /// <summary>Miraculous Scales (<c>473</c>).</summary>
    MiraculousScales = 473,
    /// <summary>General's Staff (<c>474</c>).</summary>
    GeneralsStaff = 474,
    /// <summary>Brown Crayon (<c>475</c>).</summary>
    BrownCrayon = 475,
    /// <summary>Blue Crayon (<c>476</c>).</summary>
    BlueCrayon = 476,
    /// <summary>Red Crayon (<c>477</c>).</summary>
    RedCrayon = 477,
    /// <summary>Sky-Blue Crayon (<c>478</c>).</summary>
    SkyBlueCrayon = 478,
    /// <summary>Emperor's Whip (<c>479</c>).</summary>
    EmperorsWhip = 479,
    /// <summary>Soldier's Soul (<c>480</c>).</summary>
    SoldiersSoul = 480,
    /// <summary>Poison of Power (<c>481</c>).</summary>
    PoisonOfPower = 481,
    /// <summary>Poison of Defense (<c>482</c>).</summary>
    PoisonOfDefense = 482,
    /// <summary>Poison of Speed (<c>483</c>).</summary>
    PoisonOfSpeed = 483,
    /// <summary>Poison of Agility (<c>484</c>).</summary>
    PoisonOfAgility = 484,
    /// <summary>All-Around Poison (<c>485</c>).</summary>
    AllAroundPoison = 485,
    /// <summary>Poison of Life (<c>486</c>).</summary>
    PoisonOfLife = 486,
    /// <summary>Poison of Magic (<c>487</c>).</summary>
    PoisonOfMagic = 487,
    /// <summary>Poison of Moves (<c>488</c>).</summary>
    PoisonOfMoves = 488,
    /// <summary>+1 Dagger Skill (<c>489</c>).</summary>
    Plus1DaggerSkill = 489,
    /// <summary>+5 Dagger Skill (<c>490</c>).</summary>
    Plus5DaggerSkill = 490,
    /// <summary>+1 Sword Skill (<c>491</c>).</summary>
    Plus1SwordSkill = 491,
    /// <summary>+5 Sword Skill (<c>492</c>).</summary>
    Plus5SwordSkill = 492,
    /// <summary>+1 Mace Skill (<c>493</c>).</summary>
    Plus1MaceSkill = 493,
    /// <summary>+5 Mace Skill (<c>494</c>).</summary>
    Plus5MaceSkill = 494,
    /// <summary>+1 Ax Skill (<c>495</c>).</summary>
    Plus1AxSkill = 495,
    /// <summary>+5 Ax Skill (<c>496</c>).</summary>
    Plus5AxSkill = 496,
    /// <summary>+1 Whip Skill (<c>497</c>).</summary>
    Plus1WhipSkill = 497,
    /// <summary>+5 Whip Skill (<c>498</c>).</summary>
    Plus5WhipSkill = 498,
    /// <summary>+1 Bow Skill (<c>499</c>).</summary>
    Plus1BowSkill = 499,
    /// <summary>+5 Bow Skill (<c>500</c>).</summary>
    Plus5BowSkill = 500,
    /// <summary>+1 Fire Skill (<c>501</c>).</summary>
    Plus1FireSkill = 501,
    /// <summary>+5 Fire Skill (<c>502</c>).</summary>
    Plus5FireSkill = 502,
    /// <summary>+1 Wind Skill (<c>503</c>).</summary>
    Plus1WindSkill = 503,
    /// <summary>+5 Wind Skill (<c>504</c>).</summary>
    Plus5WindSkill = 504,
    /// <summary>+1 Water Skill (<c>505</c>).</summary>
    Plus1WaterSkill = 505,
    /// <summary>+5 Water Skill (<c>506</c>).</summary>
    Plus5WaterSkill = 506,
    /// <summary>+1 Earth Skill (<c>507</c>).</summary>
    Plus1EarthSkill = 507,
    /// <summary>+5 Earth Skill (<c>508</c>).</summary>
    Plus5EarthSkill = 508,
    /// <summary>100 Sword (<c>509</c>).</summary>
    Id100Sword = 509,
    /// <summary>100 Mace (<c>510</c>).</summary>
    Id100Mace = 510,
    /// <summary>100 Fire Mace (<c>511</c>).</summary>
    Id100FireMace = 511,
}
