using UnityEngine;
using Project.Gameplay.Units;

namespace Project.Core.Commands
{
    public class MoveCommand : ICommand
    {
        private readonly UnitMover _mover;
        private readonly Vector3 _target;

        public MoveCommand(UnitMover mover, Vector3 target)
        {
            _mover = mover;
            _target = target;
        }

        public void Execute()
        {
            if (_mover != null)
                _mover.MoveTo(_target);
        }
    }
}
