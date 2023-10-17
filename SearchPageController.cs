using System.Linq;
using System.Web.Mvc;
using CompanyName.Models.Pages;
using CompanyName.Models.Pages.BaseData;
using CompanyName.ViewModels;
using EPiServer.Find;
using EPiServer.Find.Framework.Statistics;
using EPiServer.Find.Statistics;
using EPiServer.Find.UI;
using EPiServer.Find.UnifiedSearch;
using EPiServer.Globalization;
using log4net;
using packagename.Framework.CMS.Web.Controllers;
using PagedList;

namespace CompanyName.Controllers
{
    /// <summary>
    ///     Full implementation after finish story 9431 Search result page.
    ///     see more @ https://packagename.visualstudio.com/CompanyName/_workitems/edit/9431
    /// </summary>
    /// <seealso cref="EPiServer.Web.Mvc.PageController{CompanyName.Models.Pages.SearchPage}" />
    public class SearchPageController : PageControllerBase<SearchPage>
    {
        private const int MaxResults = 1000;
        private readonly IClient _searchClient;
        private readonly IFindUIConfiguration _findUIConfiguration;
        private static readonly ILog logger = LogManager.GetLogger(typeof(SearchPageController));

        public SearchPageController(
            IClient searchClient,
            IFindUIConfiguration findUIConfiguration)
        {
            _searchClient = searchClient;
            _findUIConfiguration = findUIConfiguration;
        }

        [ValidateInput(false)]
        public ActionResult Index(SearchPage currentPage, string q, int? page)
        {
            var model = new SearchContentViewModel(currentPage)
            {
                Query = (q ?? string.Empty).Trim(),
                PublicProxyPath = _findUIConfiguration.AbsolutePublicProxyPath(),
            };

            int pageIndex = page ?? 1;
            int pageSize = currentPage.PageSize;

            ITypeSearch<ISearchContent> query = null;
            UnifiedSearchResults results = null;

            if (!string.IsNullOrWhiteSpace(model.Query))
            {
                query =
                    _searchClient.UnifiedSearchFor(model.Query)
                    .UsingSynonyms()
                                //Include a facet whose value we can use to show the total number of hits
                                //regardless of section. The filter here is irrelevant but should match *everything*.
                                .TermsFacetFor(x => x.SearchSection)
                                // Don't include pages in the results if the do not include in search box is checked
                                .Filter(x => x.MatchTypeHierarchy(typeof(CompanyNameBasePageData)) & !((CompanyNameBasePageData)x).MetaData.NoIndex.Match(true))
                                // Only show results from the current language branch
                                .Filter(x => x.MatchTypeHierarchy(typeof(CompanyNameBasePageData)) & ((CompanyNameBasePageData)x).Language.Name.Match(ContentLanguage.PreferredCulture.Name))
                                .FilterFacet("AllSections", x => x.SearchSection.Exists())
                                //Fetch the specific paging page.
                                .Skip((pageIndex - 1) * pageSize)
                                .Take(pageSize)
                                .ApplyBestBets();

                var doNotTrackHeader = System.Web.HttpContext.Current.Request.Headers.Get("DNT");
                // Should Not track when value equals 1
                if (doNotTrackHeader == null || doNotTrackHeader.Equals("0"))
                {
                    query = query.Track();
                }

                var hitSpec = new HitSpecification
                {
                    HighlightTitle = model.CurrentPage.HighlightTitles,
                    HighlightExcerpt = model.CurrentPage.HighlightExcerpts
                };

                try
                {
                    results = query.GetResult(hitSpec);
                    model.Hits = new StaticPagedList<UnifiedSearchHit>(results, pageIndex, pageSize, results.TotalMatching);
                    model.RelatedQueries = _searchClient.Statistics().GetDidYouMean(model.Query).Hits?.ToList().Select(x => x.Suggestion);
                }
                catch (ServiceException ex)
                {
                    logger.Error("Episerver Find threw an exception: " + ex.Message, ex);
                    model.FindError = true;
                }
            }

            return View(model);
        }
    }
}