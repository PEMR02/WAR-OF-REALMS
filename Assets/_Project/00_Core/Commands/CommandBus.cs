using System.Collections.Generic;

namespace Project.Core.Commands
{
    public class CommandBus
    {
        private readonly Queue<ICommand> _queue = new();

        public void Enqueue(ICommand cmd)
        {
            if (cmd != null) _queue.Enqueue(cmd);
        }

        public void Flush()
        {
            while (_queue.Count > 0)
                _queue.Dequeue().Execute();
        }
    }
}
