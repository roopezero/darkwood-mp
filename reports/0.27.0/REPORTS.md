# Reports after 0.27.0

Issues the user reported after the 0.27.0 release (from play, not a numbered playtest). Triage and progress: [TRIAGE.md](TRIAGE.md).

### P01. Bug: Bumped and pushed objects don't stay in sync

When players push on objects, doors and the like, the objects don't sync properly. Dragging works: a dragged object ends up in the same place for everyone. Bumping into things doesn't: if one player bumps into an object, or pushes it into a corner, another player might not see it happen, and on their screen the object may still stand where it was at first. When that player then bumps into it, the object jumps back, for the first player too, to where it is on the second player's screen.

Objects must stay in sync when they're bumped or pushed, as they do when dragged. Verify and fix it with a single test that covers all the ways of pushing things, with several clients.

### P02. Request: Soft collision between players

Players pass through each other. They should push each other out of themselves, gently, not as a hard collision.
