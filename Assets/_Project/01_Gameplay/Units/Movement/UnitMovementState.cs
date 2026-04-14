namespace Project.Gameplay.Units.Movement
{
    public enum UnitMovementState
    {
        Idle = 0,
        PendingPath = 1,
        Moving = 2,
        Repathing = 3,
        Stuck = 4
    }
}
