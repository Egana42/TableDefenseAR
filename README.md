# TableDefense AR

Mobile AR tower defense game. The player scans a real table, a holographic board appears on it, and goblin waves walk from a portal to a crystal. Towers defend the crystal and gold coins drop on the board for the player to collect by tapping.

## Current state (demo)
- AR plane detection (AR Foundation / ARCore) and board placement on a real surface
- Board adjustment phase: move it by tapping the table, rotate it, resize it, then confirm
- Navigation mesh baked at runtime when the board is confirmed
- Goblin waves that path from the portal to the crystal, avoiding each other and obstacles (NavMesh Obstacle with carving)
- Crystal health with HUD and floating bar, Start button, Victory / Defeat screens and Retry
- Virtual towers placed with a tap that shoot the nearest goblin
- Gold coins dropped by dead goblins, collected by tapping them, used to buy towers

## Planned
- Physical tower cards (image tracking), obstacle cards and bridge cards
- Random portal and crystal positions, using the detected table surface as the play area
- More tower types (ice), balance and a Canvas-based UI
- Experiment with depth occlusion for real objects

## How to open
- Unity 6000.3.24f1 (Unity 6.3 LTS) with Android Build Support
- Scene `NevTest`: editor test scene, press Play and look at the Game tab (no camera needed)
- Scene `SampleScene`: AR scene, requires an ARCore-compatible Android phone