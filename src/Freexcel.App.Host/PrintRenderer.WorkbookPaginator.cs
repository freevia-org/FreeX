using System.Windows;
using System.Windows.Documents;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private sealed class WorkbookDocumentPaginator : DocumentPaginator
    {
        private readonly IReadOnlyList<DocumentPaginator> _paginators;
        private Size _pageSize;

        public WorkbookDocumentPaginator(IReadOnlyList<DocumentPaginator> paginators)
        {
            _paginators = paginators;
            _pageSize = paginators.FirstOrDefault()?.PageSize ?? new Size(8.27 * 96.0, 11.69 * 96.0);
        }

        public override bool IsPageCountValid => _paginators.All(paginator => paginator.IsPageCountValid);

        public override int PageCount => _paginators.Sum(paginator => paginator.PageCount);

        public override Size PageSize
        {
            get => _pageSize;
            set => _pageSize = value;
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));

            var offset = pageNumber;
            foreach (var paginator in _paginators)
            {
                if (offset < paginator.PageCount)
                    return paginator.GetPage(offset);

                offset -= paginator.PageCount;
            }

            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }
    }
}
