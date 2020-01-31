using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsvHelper;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GoogleApi;
using GoogleApi.Entities.Common;
using GoogleApi.Entities.Common.Enums;
using GoogleApi.Entities.Places.Details.Request;
using GoogleApi.Entities.Places.Photos.Request;
using GoogleApi.Entities.Places.Search.NearBy.Request;
using AirportExplorer.Model;

namespace AirportExplorer.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IWebHostEnvironment webHostEnvironment;

        public string MapboxAccessToken { get; }
        public string GoogleApiKey { get; }

        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _logger = logger;
            this.webHostEnvironment = webHostEnvironment;

            MapboxAccessToken = configuration["Mapbox:AccessToken"];
            GoogleApiKey = configuration["Google:ApiKey"];
        }

        public void OnGet()
        {

        }

        public async Task<IActionResult> OnGetAirportDetail(string name, double latitude, double longitude)
        {
            var airportDetail = new AirportDetail();

            var searchResponse = await GooglePlaces.NearBySearch.QueryAsync(
                new PlacesNearBySearchRequest
                {
                    Key = GoogleApiKey,
                    Name = name,
                    Location = new GoogleApi.Entities.Places.Search.NearBy.Request.Location(latitude, longitude),
                    Radius = 1000,
                });

            if (!searchResponse.Status.HasValue || 
                searchResponse.Status.Value != Status.Ok || 
                !searchResponse.Results.Any())
            {
                return new BadRequestResult();
            }

            var nearbyResult = searchResponse.Results.FirstOrDefault();
            string placeId = nearbyResult.PlaceId;
            string photoReference = nearbyResult.Photos?.FirstOrDefault()?.PhotoReference;
            string photoCredit = nearbyResult.Photos?.FirstOrDefault()?.HtmlAttributions.FirstOrDefault();

            var detailResponse = await GooglePlaces.Details.QueryAsync(new PlacesDetailsRequest
            {
                Key = GoogleApiKey,
                PlaceId = placeId,
            });

            if (!detailResponse.Status.HasValue || detailResponse.Status.Value != Status.Ok)
            {
                return new BadRequestResult();
            }

            var detailResult = detailResponse.Result;
            airportDetail.FormattedAddress = detailResult.FormattedAddress;
            airportDetail.PhoneNumber = detailResult.InternationalPhoneNumber;
            airportDetail.Website = detailResult.Website;

            if (photoReference != null)
            {
                var photoResponse = await GooglePlaces.Photos.QueryAsync(new PlacesPhotosRequest
                {
                    Key = GoogleApiKey,
                    PhotoReference = photoReference,
                    MaxWidth = 400,
                });

                if (photoResponse.Buffer != null)
                {
                    airportDetail.Photo = Convert.ToBase64String(photoResponse.Buffer);
                    airportDetail.PhotoCredit = photoCredit;
                }
            }

            return new JsonResult(airportDetail);
        }

        public IActionResult OnGetAirports()
        {
            var csvConfiguration = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                BadDataFound = context => { },
            };

            FeatureCollection featureCollection = new FeatureCollection();

            using (var sr = new StreamReader(Path.Combine(webHostEnvironment.WebRootPath, "airports.dat")))
            using (var reader = new CsvReader(sr, csvConfiguration))
            {
                while (reader.Read())
                {
                    string name = reader.GetField<string>(1);
                    string iataCode = reader.GetField<string>(4);
                    string latitude = reader.GetField<string>(6);
                    string longitude = reader.GetField<string>(7);

                    featureCollection.Features.Add(new Feature(
                        new Point(new Position(latitude, longitude)),
                        new Dictionary<string, object>
                        {
                            { "name", name },
                            { "iataCode", iataCode },
                        }));
                }
            }
            
            return new JsonResult(featureCollection);
        }
    }
}
