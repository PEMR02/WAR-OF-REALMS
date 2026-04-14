using UnityEngine;
using Project.Gameplay.Units;
using Project.Gameplay.Units.Movement;

namespace Project.Core.Commands
{
    public class MoveCommand : ICommand
    {
        private readonly IUnitMovementComponent _mover;
        private readonly Vector3 _target;

        public MoveCommand(IUnitMovementComponent mover, Vector3 target)
        {
            _mover = mover;
            _target = target;
        }

        public void Execute()
        {
            if (_mover != null)
                _mover.RequestMove(_target);
        }
    }
}
