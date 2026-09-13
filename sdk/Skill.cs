namespace Grandia.Sdk;

/// <summary>
/// Skill / magic ids (MapObj+0x50C+id learn bits). Same numbers
/// <see cref="CharacterEvent.Learn"/> and <see cref="MagicEvent.Id"/> use.
/// </summary>
public enum Skill
{
    /// <summary>Burn! (<c>12</c>).</summary>
    Burn = 12,
    /// <summary>Zap! (<c>40</c>).</summary>
    Zap = 40,
    /// <summary>Howl (<c>17</c>).</summary>
    Howl = 17,
    /// <summary>Runner (<c>18</c>).</summary>
    Runner = 18,
    /// <summary>Crackle (<c>35</c>).</summary>
    Crackle = 35,
    /// <summary>Freeze! (<c>36</c>).</summary>
    Freeze = 36,
    /// <summary>Heal (<c>5</c>).</summary>
    Heal = 5,
    /// <summary>Snooze (<c>7</c>).</summary>
    Snooze = 7,
    /// <summary>Cure (<c>24</c>).</summary>
    Cure = 24,
    /// <summary>Poizn (<c>23</c>).</summary>
    Poizn = 23,
    /// <summary>Stram (<c>25</c>).</summary>
    Stram = 25,
    /// <summary>Diggin' (<c>99</c>).</summary>
    Diggin = 99,
    /// <summary>Item-only: restore LV1 MP (<c>100</c>). Pearl, Cholla Flowers.</summary>
    RestoreLv1Mp = 100,
    /// <summary>Item-only: restore LV2 MP (<c>101</c>). Bamo Fruit.</summary>
    RestoreLv2Mp = 101,
    /// <summary>Item-only: restore LV3 MP (<c>102</c>). Freesia Flowers.</summary>
    RestoreLv3Mp = 102,
    /// <summary>Item-only: paralyze (<c>103</c>). Paralyze Mushroom.</summary>
    ParalyzeItem = 103,
    /// <summary>Item-only: disease (<c>104</c>). Culture Medium.</summary>
    DiseaseItem = 104,
    /// <summary>Item-only: cure paralysis (<c>105</c>). Paralysis Ointment.</summary>
    CureParalysis = 105,
    /// <summary>Item-only: vaccine / plague cure (<c>106</c>).</summary>
    CurePlague = 106,
    /// <summary>Item-only: eye drops / blindness (<c>107</c>).</summary>
    CureBlind = 107,
    /// <summary>Item-only: Torte's Whistle (<c>108</c>).</summary>
    TortesWhistle = 108,
    /// <summary>Item-only: smelling salts / wake (<c>109</c>).</summary>
    SmellingSaltsSkill = 109,
    /// <summary>Item-only: Bond of Trust (<c>110</c>).</summary>
    BondOfTrust = 110,
    /// <summary>Item-only: restore SP (party) (<c>111</c>). Ultra Drink, Golden Potion.</summary>
    RestoreSpParty = 111,
    /// <summary>Item-only: restore all MP (<c>112</c>). Blue Medicine, Magic Lamp.</summary>
    RestoreAllMp = 112,
    /// <summary>Item-only: restore all MP (party) (<c>113</c>). Spirit Potion.</summary>
    RestoreAllMpParty = 113,
    /// <summary>Item-only: restore SP (<c>114</c>). Tear Jewel, Yellow Medicine.</summary>
    RestoreSp = 114,
    /// <summary>Item-only: unblock magic (<c>115</c>). Spell Breaker.</summary>
    UnblockMagic = 115,
    /// <summary>Item-only: raise max HP in combat (<c>117</c>). Spirit Stone, Staff of Life.</summary>
    Protein = 117,
    /// <summary>Def-Loss (<c>1</c>).</summary>
    DefLoss = 1,
    /// <summary>WOW! (<c>31</c>).</summary>
    Wow = 31,
    /// <summary>BOOM! (<c>30</c>).</summary>
    Boom = 30,
    /// <summary>Burnflame (<c>13</c>).</summary>
    Burnflame = 13,
    /// <summary>Burnstrike (<c>14</c>).</summary>
    Burnstrike = 14,
    /// <summary>Zap All (<c>42</c>).</summary>
    ZapAll = 42,
    /// <summary>Howlslash (<c>19</c>).</summary>
    Howlslash = 19,
    /// <summary>Cold (<c>37</c>).</summary>
    Cold = 37,
    /// <summary>Crackling (<c>39</c>).</summary>
    Crackling = 39,
    /// <summary>Alheal (<c>6</c>).</summary>
    Alheal = 6,
    /// <summary>Healer (<c>8</c>).</summary>
    Healer = 8,
    /// <summary>Tremor (<c>2</c>).</summary>
    Tremor = 2,
    /// <summary>Gravity (<c>3</c>).</summary>
    Gravity = 3,
    /// <summary>BOOM-POW! (<c>33</c>).</summary>
    BoomPow = 33,
    /// <summary>Burnflare (<c>15</c>).</summary>
    Burnflare = 15,
    /// <summary>Fireburner (<c>16</c>).</summary>
    Fireburner = 16,
    /// <summary>DragonZap (<c>43</c>).</summary>
    DragonZap = 43,
    /// <summary>GadZap (<c>41</c>).</summary>
    GadZap = 41,
    /// <summary>Howlnado (<c>22</c>).</summary>
    Howlnado = 22,
    /// <summary>Resurrect (<c>11</c>).</summary>
    Resurrect = 11,
    /// <summary>Alhealer+ (<c>10</c>).</summary>
    AlhealerPlus = 10,
    /// <summary>Halvah (<c>29</c>).</summary>
    Halvah = 29,
    /// <summary>Quake (<c>4</c>).</summary>
    Quake = 4,
    /// <summary>BA-BOOM! (<c>34</c>).</summary>
    BaBoom = 34,
    /// <summary>Shhh! (<c>21</c>).</summary>
    Shhh = 21,
    /// <summary>Fiora (<c>38</c>).</summary>
    Fiora = 38,
    /// <summary>Protect (<c>97</c>).</summary>
    Protect = 97,
    /// <summary>Craze (<c>26</c>).</summary>
    Craze = 26,
    /// <summary>Meteor Strike (<c>32</c>).</summary>
    MeteorStrike = 32,
    /// <summary>Alhealer (<c>9</c>).</summary>
    Alhealer = 9,
    /// <summary>Magic Art (<c>44</c>).</summary>
    MagicArt = 44,
    /// <summary>Star Symphony (<c>45</c>).</summary>
    StarSymphony = 45,
    /// <summary>V-Slash (<c>50</c>).</summary>
    VSlash = 50,
    /// <summary>W-Break (<c>51</c>).</summary>
    WBreak = 51,
    /// <summary>Shockwave (<c>52</c>).</summary>
    Shockwave = 52,
    /// <summary>Midair Cut (<c>53</c>).</summary>
    MidairCut = 53,
    /// <summary>Lotus Cut (<c>56</c>).</summary>
    LotusCut = 56,
    /// <summary>Ice Slash (<c>55</c>).</summary>
    IceSlash = 55,
    /// <summary>Thor Cut (<c>57</c>).</summary>
    ThorCut = 57,
    /// <summary>Immortal Aura (<c>54</c>).</summary>
    ImmortalAura = 54,
    /// <summary>Dragon Cut (<c>74</c>).</summary>
    DragonCut = 74,
    /// <summary>Heaven and Earth (<c>59</c>).</summary>
    HeavenAndEarth = 59,
    /// <summary>Puffy Kick (<c>69</c>).</summary>
    PuffyKick = 69,
    /// <summary>Round Whacker (<c>66</c>).</summary>
    RoundWhacker = 66,
    /// <summary>Fire Away (<c>65</c>).</summary>
    FireAway = 65,
    /// <summary>Yawn (<c>68</c>).</summary>
    Yawn = 68,
    /// <summary>Puffy Fire (<c>67</c>).</summary>
    PuffyFire = 67,
    /// <summary>Knife Hurl (<c>60</c>).</summary>
    KnifeHurl = 60,
    /// <summary>Paralyze Whip (<c>62</c>).</summary>
    ParalyzeWhip = 62,
    /// <summary>Random Hurl (<c>61</c>).</summary>
    RandomHurl = 61,
    /// <summary>Fire Whip (<c>63</c>).</summary>
    FireWhip = 63,
    /// <summary>Zap! Whip (<c>64</c>).</summary>
    ZapWhip = 64,
    /// <summary>Flying Dragon Cut (<c>73</c>).</summary>
    FlyingDragonCut = 73,
    /// <summary>Eruption Cut (<c>72</c>).</summary>
    EruptionCut = 72,
    /// <summary>Mist Hide (<c>75</c>).</summary>
    MistHide = 75,
    /// <summary>Doppelganger (<c>76</c>).</summary>
    Doppelganger = 76,
    /// <summary>Dethsword (<c>77</c>).</summary>
    Dethsword = 77,
    /// <summary>Missile (<c>78</c>).</summary>
    Missile = 78,
    /// <summary>Fireball (<c>79</c>).</summary>
    Fireball = 79,
    /// <summary>Sidethrow (<c>80</c>).</summary>
    Sidethrow = 80,
    /// <summary>Discutter (<c>81</c>).</summary>
    Discutter = 81,
    /// <summary>Demon Ball (<c>82</c>).</summary>
    DemonBall = 82,
    /// <summary>Neo Demon Ball (<c>83</c>).</summary>
    NeoDemonBall = 83,
    /// <summary>Mogay Shot (<c>90</c>).</summary>
    MogayShot = 90,
    /// <summary>Mogay Bomb (<c>91</c>).</summary>
    MogayBomb = 91,
    /// <summary>Mogay Hypo (<c>92</c>).</summary>
    MogayHypo = 92,
    /// <summary>Power Up (<c>93</c>).</summary>
    PowerUp = 93,
    /// <summary>Mogay Pickpocket (<c>94</c>).</summary>
    MogayPickpocket = 94,
    /// <summary>Redshock (<c>95</c>).</summary>
    Redshock = 95,
    /// <summary>Enchantment Dance (<c>96</c>).</summary>
    EnchantmentDance = 96,
}
