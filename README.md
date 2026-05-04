# Game Project

## Overview
This project is a 2D Unity game that combines village-building elements with action running gameplay.  
The Arcade game focuses on dynamic player control, enemy interactions, and progressive difficulty through stage systems.
The Village is not set up yet (only design and a few animation).

## How to Run
([How to Play (PDF)](HowToPlayGame%20(1).pdf) has more explanation with images)
1. Download Unity

2. Type below commands in your local terminal (requires 4.4G):
```bash
cd ~/Desktop

git clone https://github.com/seanyseany/game.git
```

3. Go to Unity and find 'Projects' on the left. Click 'Add', and then click 'Add project from disk', find "game" on your Desktop.

4. Proceed the game by clicking “game"

5. Click ‘Scenes’ in Assets folder and Click ‘arcade’

6. Click play button on the top middle of the screen.

7. Space bar is jump, Down arrow key is attack (click screen if a keyboard doesn’t work)





## Features
- Player movement, combat, and multiple action states (jump, attack, rage mode)
- Various enemy types and boss mechanics with different attack patterns
- Object Pooling system for performance optimization
- Stage progression and speed scaling system
- Rage mode system with visual and gameplay changes
- UI system for health, score, and game state feedback

## Technical Highlights
- Designed modular game architecture using multiple manager classes (e.g., StageManager, GameData)
- Implemented Object Pooling to reduce runtime instantiation overhead and improve performance
- Developed scalable enemy and obstacle system for dynamic stage generation
- Managed game state and difficulty scaling through centralized control logic
- Structured scripts for reusability and maintainability

## Tech Stack
- Unity (2D)
- C#
- Object-Oriented Programming (OOP)


## My Role
- Designed overall gameplay system and architecture
- Implemented player mechanics (movement, combat, rage system)
- Built Object Pooling system for performance optimization
- Developed enemy, bosses, and obstacle interaction logic
- Created StageManager to control game progression and difficulty scaling
- Integrated multiple systems (e.g., Player types, rage mode, items interaction) into GameData

## Future Improvements
- Add save/load system for player progression
- Improve UI/UX design and player feedback
- Optimize performance further for larger-scale gameplay
- Expand village-building mechanics interacting with arcade.
