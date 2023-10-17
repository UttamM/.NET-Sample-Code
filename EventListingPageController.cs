using System.Web.Mvc;
using CompanyName.Models.Pages;
using EPiServer.Find;
using EPiServer.Find.UI;
using packagename.Framework.CMS.Web.Controllers;
using PagedList;
using log4net;
using CompanyName.ViewModels;
using EPiServer.Globalization;
using System.Collections.Generic;
using EPiServer.Find.Cms;
using System;
using CompanyName.Business.ExtensionMethods;
using EPiServer.Core;
using Castle.Core.Internal;

namespace CompanyName.Controllers
{
    /// <summary>
    /// Custom controller for event listing page.
    /// </summary>
    /// <seealso cref="packagename.Framework.CMS.Web.Controllers.PageControllerBase{CompanyName.Models.Pages.EventListingPage}" />
    public class EventListingPageController : PageControllerBase<EventListingPage>
    {
        private const int MaxResults = 1000;
        private readonly IClient _searchClient;
        private readonly IFindUIConfiguration _findUIConfiguration;
        private static readonly ILog logger = LogManager.GetLogger(typeof(EventListingPageController));

        public EventListingPageController(
            IClient searchClient,
            IFindUIConfiguration findUIConfiguration)
        {
            _searchClient = searchClient;
            _findUIConfiguration = findUIConfiguration;
        }

        [ValidateInput(false)]
        public ActionResult Index(EventListingPage currentPage, IEnumerable<int> Region, IEnumerable<int> Industry, int? page)
        {
            Region = Region ?? new List<int>();
            Industry = Industry ?? new List<int>();

            var model = new EventListingViewModel(currentPage)
            {
                Regions = Region,
                Industries = Industry,
                PublicProxyPath = _findUIConfiguration.AbsolutePublicProxyPath(),
            };

            int pageIndex = page ?? 1;
            int pageSize = currentPage.PageSize;

            try
            {
                var query = _searchClient.Search<EventsDetailPage>();
                if (!ContentReference.IsNullOrEmpty(currentPage.EventContainer))
                {
                    query = query.Filter(x => x.Ancestors().Match(currentPage.EventContainer.ToString()));
                }

                var results = query
                                .FilterForVisitor()
                                .Filter(p => p.Language.Name.Match(ContentLanguage.PreferredCulture.Name))
                                .Filter(BuildRegionIndustryFilter(model))
                                //.Filter(BuildDateFilter())
                                .ExcludeDeleted()
                                .CurrentlyPublished()
                                .ApplyBestBets()
                                .Skip((pageIndex - 1) * pageSize)
                                .Take(pageSize)
                                .OrderBy(x => x.StartDate)
                                .GetContentResult();

                model.Hits = new StaticPagedList<EventsDetailPage>(results, pageIndex, pageSize, results.TotalMatching);
            }
            catch (ServiceException ex)
            {
                logger.Error("Episerver Find threw an exception: " + ex.Message, ex);
                model.FindError = true;
            }

            return View(model);
        }

        private FilterBuilder<EventsDetailPage> BuildRegionIndustryFilter(EventListingViewModel model)
        {
            var categoryFilter = _searchClient.BuildFilter<EventsDetailPage>();
            if (model == null || (model.Regions.IsNullOrEmpty() && model.Industries.IsNullOrEmpty()))
            {
                // Select all filter
                return categoryFilter.Or(x => x.Category.In(model.Categories.ToCategoryList()));
            }

            if (model.Regions.HasValue() && model.Industries.HasValue())
            {
                return categoryFilter
                            .And(x => x.Category.In(model.Regions))
                            .And(x => x.Category.In(model.Industries));
            }

            if (model.Regions.HasValue())
            {
                return categoryFilter.Or(x => x.Category.In(model.Regions));
            }

            return categoryFilter.Or(x => x.Category.In(model.Industries));
        }

        public FilterBuilder<EventsDetailPage> BuildDateFilter()
        {
            var dateFilter = _searchClient.BuildFilter<EventsDetailPage>();
            dateFilter = dateFilter.And((x => x.StartDate.After(DateTime.Today.Date))).Or(x => x.StartDate.Match(DateTime.Today));
            return dateFilter;
        }
    }
}