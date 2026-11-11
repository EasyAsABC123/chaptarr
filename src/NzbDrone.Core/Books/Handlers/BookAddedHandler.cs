using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.MediaCover.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public class BookAddedHandler : IHandle<BookAddedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;

        public BookAddedHandler(IManageCommandQueue commandQueueManager)
        {
            _commandQueueManager = commandQueueManager;
        }

        public void Handle(BookAddedEvent message)
        {
            _commandQueueManager.Push(new DownloadBookMediaCommand(message.Book.Id));

            if (message.DoRefresh && message.Book.AuthorId > 0)
            {
                // A book request refreshes this work only; author refresh is a separate action.
                _commandQueueManager.Push(new RefreshBookCommand(message.Book.Id));
            }
        }
    }
}
