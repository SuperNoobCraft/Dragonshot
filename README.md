# Dragonshot
---

### Project Page: https://visioneers.hku.hk/project/dragonshot
### Official Trailer: https://youtu.be/UeUpObvVeRc
---

## Introduction

### Background

Dragonshot is a game designed to be played in the Cave Automatic Virtual Environment (CAVE), tested on DASECave and LEDCave located in the Human-System Interaction Simulation (HIS) Lab of the University of Hong Kong. It is developed with Unity 2021.3.45f2c1 via the VotanicXR plugin.

### Brief Overview
Dragonshot is a CAVE-based immersive archery game where the player fights a flying dragon boss. The player stands inside the CAVE, equips a bow and a difficulty quiver, and shoots arrows while physically dodging the dragon's fireballs. Crystals support the dragon in combat; destroying them and damaging the dragon within the time limit wins the fight. Getting hit by a fireball, or running out of time without finishing the dragon, loses the fight.

Secret arcade modes (Target Test and Survival) are also available alongside the main dragon fight.

---

## Gameplay Mechanics

### Equip Flow
Before the fight starts, the player picks up a floating bow with the left hand (with or without a scope), then picks a difficulty quiver (Easy / Normal / Hard) and straps it on their back. Once the quiver is mounted, the fight begins automatically. Clicking the world panel during equip (after the bow is taken) can reset the loadout so a different bow or quiver can be chosen.

### Archery
Tracked CAVE play uses the left hand for the bow and the right hand for the string. Draw power comes from how far the hands are pulled apart; releasing the string fires the arrow. Using the default bow shows a green trajectory preview. Arrow supply never runs out, but you have to keep pulling them from your back-quiver (reach behind the head to draw another arrow).

### Dragon Fight
The dragon flies within a flight volume, shielded while supporting crystals are alive. Players shoot crystals and the dragon, while the dragon launches purple fireballs toward the player. Fireballs must be dodged by physically moving in the CAVE (or avoided on desktop). Difficulty (Easy / Normal / Hard) changes HP, timer, fireball pacing, and path speed.

When the timer expires, the dragon can enter an enraged overtime chase. Victory is shown if the dragon is defeated; defeat if the player is hit by a fireball; timeout / enrage outcomes follow the fight rules on the panel.

### End-Screen Reset
At win / lose / timeout (including arcade results), a floating world reset prop can appear. Touching it with either hand (or pressing R on desktop) resets like clicking the fight panel.

---

## Gamemodes

### Dragon Fight
The intended experience. Equip a bow and a difficulty quiver, then defeat the dragon before time runs out while dodging fireballs.

| Difficulty | Time Limit | Dragon HP | Crystal Count | Dragon & Fireball Speed | Fireball Interval | Misc                              |
|------------|------------|-----------|---------------|-------------------------|-------------------|-----------------------------------|
| Easy       | 60s        | 3         | 2             | Very Slow               | Minimum 23s       | /                                 |
| Normal     | 60s        | 5         | 4             | Medium                  | Minimum 16s       | /                                 |
| Hard       | 80s        | 5         | 4             | Fast                    | Minimum 10s       | Crystals Respawn after 20 seconds |

### Arcade Mode

Before picking up anything, click the panel on the ground with your controller to enter Arcade Mode. Pick up the "No Assist" Bow and choose between **Target Test** where you try to shoot as many crystals as you can in 30 seconds, and **Survival**, where the Dragon is invincible and crystals cannot be destroyed, and the Dragon keeps shooting fireballs at the player in increasing speed. 

---

## Setup

### Launching the Game

The game is already added in the CaveLauncher of both caves. Simply select "Two Hands". If this works, you can skip the following paragraph.

This game is intended for CAVE only, and is not tested for PC environment or HMD environment. In the CAVE machines, find in the root of the game a file named VotanicXR_[CAVE][XR].bat, double click to launch. Make sure the controller is turned on when playing this game. 

Note: This game is designed to be played using CAVE glasses with a tracker, a tracked XBox Controller, and a Handtracker.

---

## Credits
- Creator: Jim Tze Lau
- Playtesters: Rafi, HongYi
- SFX and Models: Minecraft
- Skybox: https://assetstore.unity.com/packages/vfx/shaders/free-skybox-extended-shader-107400
