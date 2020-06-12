using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Fastenshtein;
using Jackett.Common.Models;
using Jackett.Common.Models.GitHub;
using Jackett.Common.Models.IndexerConfig.Bespoke;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Jackett.Common.Indexers
{
    [ExcludeFromCodeCoverage]
    public class AnimeBytes : BaseCachingWebIndexer
    {
        private string ScrapeUrl => SiteLink + "scrape.php";
        private string TorrentsUrl => SiteLink + "torrents.php";
        public bool AllowRaws => configData.IncludeRaw.Value;
        public bool PadEpisode => configData.PadEpisode != null && configData.PadEpisode.Value;
        public bool AddSynonyms => configData.AddSynonyms.Value;
        public bool FilterSeasonEpisode => configData.FilterSeasonEpisode.Value;
        public int AiringEpisode => int.Parse(configData.AiringEpisode.Value);
        private bool DevMode => true;

        public static readonly TorznabCategory TVSeries = new TorznabCategory(90010, "Anime/TV Series");
        public static readonly TorznabCategory TVSpecial = new TorznabCategory(90020, "Anime/TV Special");
        public static readonly TorznabCategory OVA = new TorznabCategory(90030, "Anime/OVA");
        public static readonly TorznabCategory ONA = new TorznabCategory(90040, "Anime/ONA");
        public static readonly TorznabCategory DVDSpecial = new TorznabCategory(90050, "Anime/DVD Special");
        public static readonly TorznabCategory BDSpecial = new TorznabCategory(90060, "Anime/BD Special");
        public static readonly TorznabCategory Movie = new TorznabCategory(90070, "Anime/Movie");

        public static readonly string AniSearchUrl = "http://anisearch.outrance.pl/";
        public static readonly string ScudLeeAnimeList = "https://rawgit.com/ScudLee/anime-lists/master/anime-list.xml";
        public static readonly string AniDBList = "http://anidb.net/api/anime-titles.xml.gz";
        public static readonly string TheXEMList = "http://thexem.de/map/allNames?origin=tvdb&seasonNumbers=1&defaultNames=1";
        public static readonly string TheXEMSingle = "http://thexem.de/map/all?";

        private static readonly Regex SpecialCharacter = new Regex(@"[`'.]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NonWord = new Regex(@"[\W]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DoubleSpace = new Regex(@"[\s]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex YearDigits = new Regex(@"\s?\(?\d{4}\)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static string Directory => Path.Combine(Path.GetTempPath(), Assembly.GetExecutingAssembly().GetName().Name.ToLower(), MethodBase.GetCurrentMethod().DeclaringType?.Name.ToLower());

        private new ConfigurationDataAnimeBytes configData
        {
            get => (ConfigurationDataAnimeBytes)base.configData;
            set => base.configData = value;
        }

        public AnimeBytes(IIndexerConfigurationService configService, Utils.Clients.WebClient client, Logger l, IProtectionService ps)
           : base(id: "animebytes",
                   name: "AnimeBytes",
                   description: "Powered by Tentacles",
                   link: "https://animebytes.tv/",
                   configService: configService,
                   client: client,
                   caps: new TorznabCapabilities(TorznabCatType.TVAnime,
                                                 TorznabCatType.Movies,
                                                 TorznabCatType.BooksComics,
                                                 TorznabCatType.ConsolePSP,
                                                 TorznabCatType.ConsoleOther,
                                                 TorznabCatType.PCGames,
                                                 TorznabCatType.AudioMP3,
                                                 TorznabCatType.AudioLossless,
                                                 TorznabCatType.AudioOther),
                   logger: l,
                   p: ps,
                   configData: new ConfigurationDataAnimeBytes("Note: Go to AnimeBytes site and open your account settings. Go to 'Account' tab, move cursor over black part near 'Passkey' and copy its value. Your username is case sensitive."))            
        {
            Encoding = Encoding.UTF8;
            Language = "en-us";
            Type = "private";

            TorznabCaps.LimitsDefault = 1000;
            TorznabCaps.LimitsMax = 1000;
            TorznabCaps.SupportsTVDBSearch = true;

            webclient.EmulateBrowser = false; // Animebytes doesn't like fake user agents (issue #1535)

            AddCategoryMapping("anime[tv_series]", TorznabCatType.TVAnime, "TV Series");
            AddCategoryMapping("anime[tv_special]", TorznabCatType.TVAnime, "TV Special");
            AddCategoryMapping("anime[ova]", TorznabCatType.TVAnime, "OVA");
            AddCategoryMapping("anime[ona]", TorznabCatType.TVAnime, "ONA");
            AddCategoryMapping("anime[dvd_special]", TorznabCatType.TVAnime, "DVD Special");
            AddCategoryMapping("anime[bd_special]", TorznabCatType.TVAnime, "BD Special");
            AddCategoryMapping("anime[movie]", TorznabCatType.Movies, "Movie");
            AddCategoryMapping("gamec[game]", TorznabCatType.PCGames, "Game");
            AddCategoryMapping("gamec[visual_novel]", TorznabCatType.PCGames, "Visual Novel");
            AddCategoryMapping("printedtype[manga]", TorznabCatType.BooksComics, "Manga");
            AddCategoryMapping("printedtype[oneshot]", TorznabCatType.BooksComics, "Oneshot");
            AddCategoryMapping("printedtype[anthology]", TorznabCatType.BooksComics, "Anthology");
            AddCategoryMapping("printedtype[manhwa]", TorznabCatType.BooksComics, "Manhwa");
            AddCategoryMapping("printedtype[light_novel]", TorznabCatType.BooksComics, "Light Novel");
            AddCategoryMapping("printedtype[artbook]", TorznabCatType.BooksComics, "Artbook");

            AddCategoryMapping("anime[tv_series]", AnimeBytes.TVSeries, "Anime/TV Series");
            AddCategoryMapping("anime[tv_special]", AnimeBytes.TVSpecial, "Anime/TV Special");
            AddCategoryMapping("anime[ova]", AnimeBytes.OVA, "Anime/OVA");
            AddCategoryMapping("anime[ona]", AnimeBytes.ONA, "Anime/ONA");
            AddCategoryMapping("anime[dvd_special]", AnimeBytes.DVDSpecial, "Anime/DVD Special");
            AddCategoryMapping("anime[bd_special]", AnimeBytes.BDSpecial, "Anime/BD Special");
            AddCategoryMapping("anime[movie]", AnimeBytes.Movie, "Anime/Movie");

        }
        // Prevent filtering
        protected override IEnumerable<ReleaseInfo> FilterResults(TorznabQuery query, IEnumerable<ReleaseInfo> input) =>
            input;

        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            LoadValuesFromJson(configJson);

            if (configData.Passkey.Value.Length != 32 && configData.Passkey.Value.Length != 48)
                throw new Exception("invalid passkey configured: expected length: 32 or 48, got " + configData.Passkey.Value.Length.ToString());

            var results = await PerformQuery(new TorznabQuery());
            if (results.Count() == 0)
            {
                throw new Exception("no results found, please report this bug");
            }

            IsConfigured = true;
            SaveConfig();
            return IndexerConfigurationStatus.Completed;
        }

        private string StripEpisodeNumber(string term)
        {
            // Tracer does not support searching with episode number so strip it if we have one
            term = Regex.Replace(term, @"\W(\dx)?\d?\d$", string.Empty);
            term = YearDigits.Replace(term, "");
            //term = Regex.Replace(term, @"\W(S\d\d?E)?\d?\d$", string.Empty);
            return term;
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            // The result list
            var releases = new List<ReleaseInfo>();

            if (ContainsMusicCategories(query.Categories))
            {
                foreach (var result in await GetResults(query, "music", query.SanitizedSearchTerm))
                {
                    releases.Add(result);
                }
            }

            foreach (var result in await GetResults(query, "anime", StripEpisodeNumber(query.SanitizedSearchTerm)))
            {
                releases.Add(result);
            }

            return releases.ToArray();
        }

        private bool ContainsMusicCategories(int[] categories)
        {
            var music = new[]
            {
                TorznabCatType.Audio.ID,
                TorznabCatType.AudioMP3.ID,
                TorznabCatType.AudioLossless.ID,
                TorznabCatType.AudioOther.ID,
                TorznabCatType.AudioForeign.ID
            };

            return categories.Length == 0 || music.Any(categories.Contains);
        }

        private IEnumerable<ReleaseInfo> FilterCachedQuery(TorznabQuery query, CachedQueryResult cachedResult)
        {
            string absoluteEpisode = "";
            Match absoluteEpisodeMatch = Regex.Match(query.SanitizedSearchTerm, @"\W(\d+)");
            if (absoluteEpisodeMatch.Success)
            {
                // Check if requested episode is in cached result
                absoluteEpisode = absoluteEpisodeMatch.Groups[1].Value;

                var newResults = cachedResult.Results.Where(s => s.Title.Contains(absoluteEpisode)).ToList();

                // If it's empty, it's not in cached result. Just return everything
                //return newResults.IsEmpty() ? cachedResult.Results : newResults;
                return newResults.Any() ? newResults : cachedResult.Results;
            }
            else
                return cachedResult.Results;
        }

        private async Task<AnimeReleaseInfo> GetDBInfoAsync(string titleName)
        {
            // Create Directory if not exist
            System.IO.Directory.CreateDirectory(Directory);

            // Clean Storage Provider Directory from outdated cached queries
            CleanCacheStorage();

            // File Name
            Dictionary<string, string> dbFilesUrl = new Dictionary<string, string>();
            dbFilesUrl.Add("anidb", AnimeBytes.AniDBList);
            dbFilesUrl.Add("scudlee", AnimeBytes.ScudLeeAnimeList);
            dbFilesUrl.Add("thexem", AnimeBytes.TheXEMList);

            Dictionary<string, string> dbFilePaths = new Dictionary<string, string>();
            foreach (var file in dbFilesUrl)
            {
                string fileName = "";

                // temporary hack
                if (file.Key == "thexem")
                    fileName = "thexem.json";
                else
                    fileName = Path.GetFileName(file.Value);

                var filePath = Path.Combine(Directory, fileName);

                FileInfo fileObj = new FileInfo(filePath);
                
                if (!File.Exists(filePath))
                {
                    output("Downloading " + fileName);
                    try
                    {
                        System.Net.WebClient wClient = new WebClient();
                        wClient.Headers.Add("user-agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/83.0.4103.97 Safari/537.36");
                        wClient.DownloadFile(file.Value, filePath);

                        if (Path.GetExtension(fileName) == ".gz")
                        {
                            string newFilePath = filePath.Remove(filePath.Length - fileObj.Extension.Length);

                            using (FileStream originalFileStream = fileObj.OpenRead())
                            using (var decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                            using (var reader = new StreamReader(decompressionStream, Encoding.UTF8, true))
                            using (var fileStream = File.Open(newFilePath, FileMode.Create, FileAccess.Write))
                            using (var writer = new StreamWriter(fileStream))
                            {
                                var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                                text = text.Replace("&#x0;", "");

                                await writer.WriteAsync(text).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        output("Error downloading " + fileName + " " + e.Message, "error");
                        return null;
                    }
                }

                if (Path.GetExtension(fileName) == ".gz")
                    filePath = filePath.Remove(filePath.Length - fileObj.Extension.Length);

                dbFilePaths.Add(file.Key, filePath);
            }

            AnimeReleaseInfo results = await ParseAnimeTitleAsync(titleName, dbFilePaths);
            
            return results;
        }

        private async Task<AnimeReleaseInfo> ParseAnimeTitleAsync(string titleName, Dictionary<string, string> dbFilePaths)
        {
            AnimeReleaseInfo releaseInfo = new AnimeReleaseInfo();

            try
            {
                if (string.IsNullOrWhiteSpace(titleName))
                    return releaseInfo;

                Match episodeMatch = Regex.Match(titleName, @"\W(\d+)$");
                if (episodeMatch.Success)
                {
                    releaseInfo.Episode = int.Parse(episodeMatch.Groups[1].Value);
                    titleName = StripEpisodeNumber(titleName);
                }

                var aniDB = XDocument.Parse(File.ReadAllText(dbFilePaths["anidb"]));
                var scudLee = XDocument.Parse(File.ReadAllText(dbFilePaths["scudlee"]));
                var theXEM = File.ReadAllText(dbFilePaths["thexem"]);

                // First, Obtain the TVDbId
                var xemAnimeList = JsonConvert.DeserializeObject<XEMRoot>(theXEM).data;

                var releaseInfoList = xemAnimeList.Where(s => s.Value.defaultName.Equals(titleName) || s.Value.alternativeNames.ContainsKey(titleName))
                    .Select(s => ConvertToAnimeReleaseInfo(titleName, s, releaseInfo)).ToList();

                if (!releaseInfoList.Any())
                {
                    // We can't find it, we'll need to try harder
                    // First map those without alternative names to have at least one alternativenames
                    var filterAnimeList = xemAnimeList.Where(s => !s.Value.alternativeNames.ToList().Any());
                    foreach (var anime in filterAnimeList)
                        anime.Value.alternativeNames.Add(anime.Value.defaultName, 1);


                    // Rank the closest match by Levenshtein distance
                    var sortedReleaseInfoList = xemAnimeList.Select(s => new
                    {
                        TVDBID = s.Key,
                        EditDistance = s.Value.alternativeNames.Select(i => new
                        {
                            L = Levenshtein.Distance(titleName, i.Key),
                            Name = i.Key,
                            Season = i.Value
                        }).OrderBy(j => j.L).First()
                    }).OrderBy(s => s.EditDistance.L).ToList();

                    if (sortedReleaseInfoList.Any())
                    {
                        var first = sortedReleaseInfoList.First();
                        releaseInfo.TVDBId = first.TVDBID;
                        releaseInfo.SearchTitle = first.EditDistance.Name;
                        releaseInfo.SceneSeason = first.EditDistance.Season;
                    }
                }

                // We need to check if SceneSeason and Season are the same
                if (releaseInfo.SceneSeason != null && releaseInfo.TVDBId != null)
                {
                    var anyAniDBIDList = scudLee.Descendants("anime").Where(s => s.Attribute("tvdbid").Value.Equals(releaseInfo.TVDBId.ToString()));

                    // Filter off specials if we are looking for TV Series
                    if (releaseInfo.SceneSeason > 0)
                        anyAniDBIDList = anyAniDBIDList.Where(s => !s.Attribute("defaulttvdbseason").Value.Equals("0"));

                    if (anyAniDBIDList.Any())
                    {
                        // Find best match for narrowing down AniDBID
                        /*var sortedReleaseInfoList = anyAniDBIDList.Select(s => new
                        {
                            ANIDBID = s.Attribute("anidbid").Value,
                            Name = s.Element("name").Value,
                            EditDistance = Levenshtein.Distance(titleName, s.Element("name").Value)
                        }).OrderBy(s => s.EditDistance).ToList();*/

                        // Use season to match
                        var currentAniDB = anyAniDBIDList.ToList()[(releaseInfo.SceneSeason ?? 1) - 1];
                        var aaid = int.Parse(currentAniDB.Attribute("anidbid").Value);

                        var queryCollection = new NameValueCollection();
                        //var currentAniDB = sortedReleaseInfoList.First();

                        releaseInfo.AniDBID = aaid;
                        queryCollection.Add("id", aaid.ToString());
                        queryCollection.Add("origin", "anidb");
                        queryCollection.Add("destination", "tvdb");
                        //queryCollection.Add("episode", releaseInfo.Episode?.ToString() ?? "1");
                        //queryCollection.Add("season", releaseInfo.SceneSeason.ToString());
                        
                        var queryUrl = TheXEMSingle + queryCollection.GetQueryString();

                        // Query TheXEM for TVDB's Season
                        var response = await RequestStringWithCookiesAndRetry(queryUrl);
                        if (!response.Content.StartsWith("{")) // not JSON => error
                            throw new ExceptionWithConfigData("unexcepted response (not JSON)", configData);
                        dynamic json = JsonConvert.DeserializeObject<dynamic>(response.Content);

                        //releaseInfo.Season = (int) json["data"]["tvdb"]["season"];
                        var mappings = json["data"];
                        foreach(var maps in mappings)
                        {
                            if (maps["anidb"]["season"] != releaseInfo.SceneSeason || maps["tvdb"]["episode"] != releaseInfo.Episode)
                                continue;

                            releaseInfo.Season = maps["tvdb"]["season"];
                            break;
                        }
                    }
                }

                // Check if ScudLee has what we're looking for
                //var scudSearch = scudLee.Descendants("anime").Where(s => NormalizeToSonarrNames(s.Element("name").Value).Equals(titleName)).ToList();

                /*if (!scudSearch.IsEmpty())
                {
                    var firstScud = scudSearch.First();
                    releaseInfo.AniDBID = int.Parse(firstScud.Attribute("anidbid").Value);
                    releaseInfo.TVDBId = int.Parse(firstScud.Attribute("tvdbid").Value);

                    if (int.TryParse(firstScud.Attribute("defaulttvdbseason").Value, out int seasonInt))
                    {
                        releaseInfo.Season = seasonInt;
                    }

                    var aniDBJPName = aniDB.Descendants("anime")
                        .Where(s => s.Attribute("aid").Value.Equals(releaseInfo.AniDBID.ToString()))
                        .Select(s => new
                        {
                            jpTitle = s.Elements("title").Where(i => i.Attribute(XNamespace.Xml + "lang").Value.Equals("ja") && i.Attribute("type").Value.Equals("official")).First()
                        }).First();

                    releaseInfo.SearchTitle = RemoveYear(aniDBJPName.jpTitle.Value);

                    return releaseInfo;
                }
                else
                {
                    // Let's try a little harder
                    var filterScudLee = scudLee.Descendants("anime")
                        .Where(s => Regex.Match(s.Attribute("tvdbid").Value, @"\d+").Success) // We don't do OVAs
                        .Where(s => Regex.Match(NormalizeToSonarrNames(s.Element("name").Value), titleName, RegexOptions.IgnoreCase).Success) // Partial matching
                        .Select(s => new
                    {
                        AniDBID = s.Attribute("anidbid").Value,
                        TVDBID = s.Attribute("tvdbid").Value,
                        EditDistance = Levenshtein.Distance(titleName, NormalizeToSonarrNames(s.Element("name").Value)),
                        Name = s.Element("name").Value,
                        Season = s.Attribute("defaulttvdbseason").Value
                    }).OrderBy(s => s.EditDistance).ToList();
                    
                    if (!filterScudLee.IsEmpty())
                    {
                        try
                        {
                            var first = filterScudLee.First();
                            releaseInfo.AniDBID = int.Parse(first.AniDBID);
                            releaseInfo.TVDBId = int.Parse(first.TVDBID);

                            if (int.TryParse(first.Season, out int seasonInt))
                                releaseInfo.Season = seasonInt;
                            else if (first.Season == "a")
                                releaseInfo.Season = 1;

                            var aniDBJPName = aniDB.Descendants("anime")
                                .Where(s => s.Attribute("aid").Value.Equals(releaseInfo.AniDBID.ToString()))
                                .Select(s => new
                                {
                                    jpTitle = s.Elements("title").Where(i => i.Attribute(XNamespace.Xml + "lang").Value.Equals("ja") && i.Attribute("type").Value.Equals("official")).First()
                                }).First();

                            releaseInfo.SearchTitle = RemoveYear(aniDBJPName.jpTitle.Value);

                            return releaseInfo;
                        }
                        catch (Exception)
                        {
                            // We will skip this
                        }
                    }
                }*/

                // Map it to exact AniDBID if it's missing
                /*if (releaseInfo.AniDBID == null)
                {
                    var scudAnime = scudLee.Descendants("anime").Where(s => s.Attribute("tvdbid").Value.Equals(releaseInfo.TVDBId.ToString())
                    && s.Attribute("defaulttvdbseason").Value.Equals(releaseInfo.Season.ToString())).ToList();

                    if (scudAnime.Any())
                    {
                        var candidate = scudAnime.First();
                        foreach (var s in scudAnime)
                        {
                            if (s.Attribute("episodeoffset") != null && int.Parse(s.Attribute("episodeoffset").Value) <= releaseInfo.Episode)
                            {
                                candidate = s;
                                break;
                            }
                        }
                        releaseInfo.AniDBID = int.Parse(candidate.Attribute("anidbid").Value);
                    }
                }*/
                

                /*if (releaseInfo.SearchTitle == null)
                    releaseInfo.SearchTitle = releaseInfo.Title;*/

                // Get Japanese search title
                var aniDBJPName = aniDB.Descendants("anime")
                                .Where(s => s.Attribute("aid").Value.Equals(releaseInfo.AniDBID.ToString()))
                                .Select(s => new
                                {
                                    jpTitle = s.Elements("title").Where(i => i.Attribute(XNamespace.Xml + "lang").Value.Equals("ja") && i.Attribute("type").Value.Equals("official")).First()
                                }).First();

                releaseInfo.SearchTitle = RemoveYear(aniDBJPName.jpTitle.Value);
            }
            catch (Exception)
            {
                output("Caught Exception");
                // Whatever happens, just skip using this method
            }            

            return releaseInfo;
        }

        public static string NormalizeToSonarrNames(string cleanTitle)
        {
            cleanTitle = cleanTitle.Replace("&", "and");
            cleanTitle = SpecialCharacter.Replace(cleanTitle, "");
            cleanTitle = NonWord.Replace(cleanTitle, " ");
            cleanTitle = DoubleSpace.Replace(cleanTitle, " ");
            return cleanTitle;
        }

        public static string RemoveYear(string cleanTitle)
        {
            cleanTitle = YearDigits.Replace(cleanTitle, "");
            return cleanTitle;
        }

        private AnimeReleaseInfo ConvertToAnimeReleaseInfo(string titleName, KeyValuePair<int, XEMAnime> anime, AnimeReleaseInfo animeReleaseInfo)
        {
            animeReleaseInfo.TVDBId = anime.Key;
            animeReleaseInfo.BaseTitle = anime.Value.defaultName;
            
            if (anime.Value.alternativeNames.ContainsKey(titleName))
            {
                animeReleaseInfo.Season = anime.Value.alternativeNames[titleName];
                animeReleaseInfo.Title = titleName;
            }

            return animeReleaseInfo;
        }

        private void CleanCacheStorage(bool force = false)
        {
            // Check cleaning method
            if (force)
            {
                // Deleting Provider Storage folder and all files recursively
                output("\nDeleting Anime List Cache File...");

                // Check if directory exist
                if (System.IO.Directory.Exists(Directory))
                {
                    // Delete storage directory of provider
                    System.IO.Directory.Delete(Directory, true);
                    output("-> Anime List Cache deleted successfully.");
                }
                else
                {
                    // No directory, so nothing to do
                    output("-> No Anime List Cache folder found for this provider !");
                }
            }
            else
            {
                var i = 0;
                // Check if there is file older than ... and delete them
                output("\nCleaning Anime List Cache folder... in progress.");
                System.IO.Directory.GetFiles(Directory)
                .Select(f => new FileInfo(f))
                .Where(f => f.LastAccessTime < DateTime.Now.AddMilliseconds(-Convert.ToInt32(configData.HardDriveCacheKeepTime.Value)))
                .ToList()
                .ForEach(f =>
                {
                    output("Deleting cached file << " + f.Name + " >> ... done.");
                    f.Delete();
                    i++;
                });

                // Inform on what was cleaned during process
                if (i > 0)
                {
                    output("-> Deleted " + i + " cached files during cleaning.");
                }
                else
                {
                    output("-> Nothing deleted during cleaning.");
                }
            }
        }

        /// <summary>
        /// Output message for logging or developpment (console)
        /// </summary>
        /// <param name="message">Message to output</param>
        /// <param name="level">Level for Logger</param>
        private void output(string message, string level = "debug")
        {
            // Check if we are in dev mode
            if (DevMode)
            {
                // Output message to console
                Console.WriteLine(message);
            }
            else
            {
                // Send message to logger with level
                switch (level)
                {
                    default:
                        goto case "debug";
                    case "debug":
                        // Only if Debug Level Enabled on Jackett
                        if (logger.IsDebugEnabled)
                        {
                            logger.Debug(message);
                        }
                        break;

                    case "info":
                        logger.Info(message);
                        break;

                    case "error":
                        logger.Error(message);
                        break;
                }
            }
        }

        private async Task<IEnumerable<ReleaseInfo>> GetResults(TorznabQuery query, string searchType, string searchTerm)
        {
            // The result list
            var releases = new List<ReleaseInfo>();
            AnimeReleaseInfo animeReleaseInfo = null;

            var queryCollection = new NameValueCollection();

            var queryCats = MapTorznabCapsToTrackers(query);
            if (queryCats.Count > 0)
            {
                foreach (var cat in queryCats)
                {
                    queryCollection.Add(cat, "1");
                }
            }
            if (AiringEpisode >= 0)
                queryCollection.Add("airing", AiringEpisode.ToString());

            if (searchType == "anime")
            {
                animeReleaseInfo = await GetDBInfoAsync(query.SanitizedSearchTerm);
                //searchTerm = animeReleaseInfo.BaseTitle.IsNullOrEmptyOrWhitespace() || animeReleaseInfo.AniDBID == null ? searchTerm : animeReleaseInfo.BaseTitle;
                //searchTerm = animeReleaseInfo.SearchTitle.IsNullOrEmptyOrWhitespace() ? searchTerm : animeReleaseInfo.SearchTitle;
                searchTerm = string.IsNullOrWhiteSpace(animeReleaseInfo.SearchTitle) ? searchTerm : animeReleaseInfo.SearchTitle;
            }                

            queryCollection.Add("username", configData.Username.Value);
            queryCollection.Add("torrent_pass", configData.Passkey.Value);
            queryCollection.Add("type", searchType);
            queryCollection.Add("searchstr", searchTerm);
            var queryUrl = ScrapeUrl + "?" + queryCollection.GetQueryString();

            //var anisearchQueryCollection = new NameValueCollection();
            //anisearchQueryCollection.Add("task", "search");
            //anisearchQueryCollection.Add("query", "\\" + searchTerm);
            //anisearchPage = RequestStringWithCookiesAndRetry()

            // Check cache first so we don't query the server for each episode when searching for each episode in a series.
            /*lock (cache)
            {
                // Remove old cache items
                CleanCache();

                var cachedResult = cache.Where(i => i.Query == queryUrl).FirstOrDefault();
                if (cachedResult != null)
                {
                    var filteredCachedResult = FilterCachedQuery(query, cachedResult);
                    return filteredCachedResult.Select(s => (ReleaseInfo)s.Clone()).ToArray();
                }
            }*/

            // Get the content from the tracker
            var response = await RequestStringWithCookiesAndRetry(queryUrl);
            if (!response.Content.StartsWith("{")) // not JSON => error
                throw new ExceptionWithConfigData("unexcepted response (not JSON)", configData);
            dynamic json = JsonConvert.DeserializeObject<dynamic>(response.Content);

            // Parse
            try
            {
                if (json["error"] != null)
                    throw new Exception(json["error"].ToString());

                var Matches = (long)json["Matches"];

                if (Matches > 0)
                {
                    var groups = (JArray)json.Groups;

                    foreach (JObject group in groups)
                    {
                        // AniDBID Matching
                        int? aniDBID = null;
                        IDictionary<string, string> links = null;

                        if (group["Links"].HasValues)
                            links = group["Links"].ToObject<Dictionary<string, string>>();
                                                
                        if (links != null && links.ContainsKey("AniDB"))
                        {
                            var AniDBIDRegEx = new Regex(@"anidb.net/anime/.*?(\d+)$");
                            var AniDBIDMatch = AniDBIDRegEx.Match(links["AniDB"]);
                            if (AniDBIDMatch.Success)
                                aniDBID = ParseUtil.CoerceInt(AniDBIDMatch.Groups[1].Value);
                        }

                        if (aniDBID != null && animeReleaseInfo.AniDBID != null && aniDBID != animeReleaseInfo.AniDBID)
                            continue;

                        var synonyms = new List<string>();
                        var groupID = (long)group["ID"];
                        var Image = (string)group["Image"];
                        var ImageUrl = (string.IsNullOrWhiteSpace(Image) ? null : new Uri(Image));
                        var Year = (int)group["Year"];
                        var GroupName = (string)group["GroupName"];
                        var SeriesName = (string)group["SeriesName"];
                        var mainTitle = WebUtility.HtmlDecode((string)group["FullName"]);
                        if (SeriesName != null)
                            mainTitle = SeriesName;

                        // Override main title if base title exists.
                        /*if (!animeReleaseInfo.BaseTitle.IsNullOrEmptyOrWhitespace())
                        {
                            mainTitle = animeReleaseInfo.BaseTitle;
                        }*/

                        synonyms.Add(mainTitle);
                        if (AddSynonyms)
                        {
                            foreach (string synonym in group["Synonymns"])
                                synonyms.Add(synonym);
                        }

                        List<int> Category = null;
                        var category = (string)group["CategoryName"];

                        var Description = (string)group["Description"];

                        foreach (JObject torrent in group["Torrents"])
                        {
                            var releaseInfo = "";
                            if (animeReleaseInfo.Season != null)
                                releaseInfo = string.Format("S{0:00}", animeReleaseInfo.Season);                
                            
                            string episode = null;
                            int? season = null;
                            var EditionTitle = (string)torrent["EditionData"]["EditionTitle"];
                            if (!string.IsNullOrWhiteSpace(EditionTitle))
                                releaseInfo = WebUtility.HtmlDecode(EditionTitle);

                            var SeasonRegEx = new Regex(@"Season (\d+)", RegexOptions.Compiled);
                            var SeasonRegExMatch = SeasonRegEx.Match(releaseInfo);
                            if (SeasonRegExMatch.Success)
                                season = ParseUtil.CoerceInt(SeasonRegExMatch.Groups[1].Value);

                            var EpisodeRegEx = new Regex(@"Episode (\d+)", RegexOptions.Compiled);
                            var EpisodeRegExMatch = EpisodeRegEx.Match(releaseInfo);
                            if (EpisodeRegExMatch.Success)
                                episode = EpisodeRegExMatch.Groups[1].Value;

                            releaseInfo = releaseInfo.Replace("Episode ", "");
                            releaseInfo = releaseInfo.Replace("Season ", "S");
                            releaseInfo = Regex.Replace(releaseInfo, @"\(\d+-\d+\)", "");
                            releaseInfo = releaseInfo.Trim();

                            if (PadEpisode && int.TryParse(releaseInfo, out var test) && releaseInfo.Length == 1)
                            {
                                releaseInfo = "0" + releaseInfo;
                            }

                            if (FilterSeasonEpisode)
                            {
                                if (query.Season != 0 && season != null && season != query.Season) // skip if season doesn't match
                                    continue;
                                if (query.Episode != null && episode != null && episode != query.Episode) // skip if episode doesn't match
                                    continue;
                            }
                            var torrentID = (long)torrent["ID"];
                            var Property = (string)torrent["Property"];
                            Property = Property.Replace(" | Freeleech", "");
                            var Link = (string)torrent["Link"];
                            var LinkUri = new Uri(Link);
                            var UploadTimeString = (string)torrent["UploadTime"];
                            var UploadTime = DateTime.ParseExact(UploadTimeString, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                            var PublushDate = DateTime.SpecifyKind(UploadTime, DateTimeKind.Utc).ToLocalTime();
                            var CommentsLink = SiteLink + "torrent/" + torrentID.ToString() + "/group";
                            var CommentsLinkUri = new Uri(CommentsLink);
                            var Size = (long)torrent["Size"];
                            var Snatched = (long)torrent["Snatched"];
                            var Seeders = (int)torrent["Seeders"];
                            var Leechers = (int)torrent["Leechers"];
                            var FileCount = (long)torrent["FileCount"];
                            var Peers = Seeders + Leechers;

                            var RawDownMultiplier = (int?)torrent["RawDownMultiplier"];
                            if (RawDownMultiplier == null)
                                RawDownMultiplier = 0;
                            var RawUpMultiplier = (int?)torrent["RawUpMultiplier"];
                            if (RawUpMultiplier == null)
                                RawDownMultiplier = 0;

                            if (searchType == "anime")
                            {
                                if (GroupName == "TV Series" || GroupName == "OVA")
                                    Category = new List<int> { TorznabCatType.TVAnime.ID };

                                // Ignore these categories as they'll cause hell with the matcher
                                // TV Special, OVA, ONA, DVD Special, BD Special

                                if (GroupName == "Movie" || GroupName == "Live Action Movie")
                                    Category = new List<int> { TorznabCatType.Movies.ID };

                                if (category == "Manga" || category == "Oneshot" || category == "Anthology" || category == "Manhwa" || category == "Manhua" || category == "Light Novel")
                                    Category = new List<int> { TorznabCatType.BooksComics.ID };

                                if (category == "Novel" || category == "Artbook")
                                    Category = new List<int> { TorznabCatType.BooksComics.ID };

                                if (category == "Game" || category == "Visual Novel")
                                {
                                    if (Property.Contains(" PSP "))
                                        Category = new List<int> { TorznabCatType.ConsolePSP.ID };
                                    if (Property.Contains("PSX"))
                                        Category = new List<int> { TorznabCatType.ConsoleOther.ID };
                                    if (Property.Contains(" NES "))
                                        Category = new List<int> { TorznabCatType.ConsoleOther.ID };
                                    if (Property.Contains(" PC "))
                                        Category = new List<int> { TorznabCatType.PCGames.ID };
                                }
                            }
                            else if (searchType == "music")
                            {
                                if (category == "Single" || category == "EP" || category == "Album" || category == "Compilation" || category == "Soundtrack" || category == "Remix CD" || category == "PV" || category == "Live Album" || category == "Image CD" || category == "Drama CD" || category == "Vocal CD")
                                {
                                    if (Property.Contains(" Lossless "))
                                        Category = new List<int> { TorznabCatType.AudioLossless.ID };
                                    else if (Property.Contains("MP3"))
                                        Category = new List<int> { TorznabCatType.AudioMP3.ID };
                                    else
                                        Category = new List<int> { TorznabCatType.AudioOther.ID };
                                }
                            }

                            // We dont actually have a release name >.> so try to create one
                            var releaseTags = Property.Split("|".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).ToList();
                            for (var i = releaseTags.Count - 1; i >= 0; i--)
                            {
                                releaseTags[i] = releaseTags[i].Trim();
                                if (string.IsNullOrWhiteSpace(releaseTags[i]))
                                    releaseTags.RemoveAt(i);
                            }

                            var releasegroup = releaseTags.LastOrDefault();
                            if (releasegroup != null && releasegroup.Contains("(") && releasegroup.Contains(")"))
                            {
                                // Skip raws if set
                                if (releasegroup.ToLowerInvariant().StartsWith("raw") && !AllowRaws)
                                {
                                    continue;
                                }

                                var start = releasegroup.IndexOf("(");
                                releasegroup = "[" + releasegroup.Substring(start + 1, (releasegroup.IndexOf(")") - 1) - start) + "] ";
                            }
                            else
                            {
                                releasegroup = string.Empty;
                            }
                            if (!AllowRaws && releaseTags.Contains("raw", StringComparer.InvariantCultureIgnoreCase))
                                continue;

                            var infoString = releaseTags.Aggregate("", (prev, cur) => prev + "[" + cur + "]");
                            var MinimumSeedTime = 259200;
                            //  Additional 5 hours per GB
                            MinimumSeedTime += (int)((Size / 1000000000) * 18000);

                            foreach (var title in synonyms)
                            {
                                string releaseTitle = null;
                                if (GroupName == "Movie")
                                {
                                    releaseTitle = string.Format("{0} {1} {2}{3}", title, Year, releasegroup, infoString);
                                }
                                else
                                {
                                    releaseTitle = string.Format("{0}{1} {2} {3}", releasegroup, title, releaseInfo, infoString);
                                }

                                var guid = new Uri(CommentsLinkUri + "&nh=" + StringUtil.Hash(title));
                                var release = new ReleaseInfo
                                {
                                    MinimumRatio = 1,
                                    MinimumSeedTime = MinimumSeedTime,
                                    Title = releaseTitle,
                                    Comments = CommentsLinkUri,
                                    Guid = guid, // Sonarr should dedupe on this url - allow a url per name.
                                    Link = LinkUri,
                                    BannerUrl = ImageUrl,
                                    PublishDate = PublushDate,
                                    Category = Category,
                                    Description = Description,
                                    Size = Size,
                                    Seeders = Seeders,
                                    Peers = Peers,
                                    Grabs = Snatched,
                                    Files = FileCount,
                                    DownloadVolumeFactor = RawDownMultiplier,
                                    UploadVolumeFactor = RawUpMultiplier
                                };

                                releases.Add(release);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnParseError(response.Content, ex);
            }

            // Add to the cache
            lock (cache)
            {
                cache.Add(new CachedQueryResult(queryUrl, releases));
            }

            return releases.Select(s => (ReleaseInfo)s.Clone());
        }

        public class XEMRoot
        {
            [JsonProperty("result")]
            public string result { get; set; }

            [JsonProperty("data")]
            public IDictionary<int, XEMAnime> data { get; set; }

            [JsonProperty("message")]
            public string message { get; set; }
        }

        [JsonConverter(typeof(XEMConvert))]
        public class XEMAnime
        {
            public string defaultName { get; set; }
            public Dictionary<string, int> alternativeNames { get; set; }

            public XEMAnime()
            {
                alternativeNames = new Dictionary<string, int>();
            }
        }

        public class XEMConvert : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return (objectType == typeof(XEMAnime));
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                JArray ja = new JArray();
                XEMAnime anime = (XEMAnime) value;
                ja.Add(anime.defaultName);
                ja.Add(anime.alternativeNames);
                ja.WriteTo(writer);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                JArray ja = JArray.Load(reader);
                XEMAnime anime = new XEMAnime();
                anime.defaultName = (string) ja[0];
                
                for (var i = 1; i < ja.Count; i++)
                {
                    foreach(var property in ja[i].ToObject<JObject>().Properties())
                    {
                        try
                        {
                            anime.alternativeNames.Add((string)property.Name, (int)property.Value);
                        } catch (ArgumentException)
                        {
                            // Duplicate Key Found, Skip it.
                        }
                    }   
                }

                return anime;
            }

        }
    
}

    public class AnimeReleaseInfo : ReleaseInfo
    {
        public string BaseTitle { get; set; }
        public long? BaseAniDBID { get; set; }
        public int? AniDBID { get; set; }
        public int? Season { get; set; }
        public int? SceneSeason { get; set; }
        public string SearchTitle { get; set; }
        public int? Episode { get; set;  }

        public AnimeReleaseInfo() { }

        protected AnimeReleaseInfo(AnimeReleaseInfo copyFrom) : base(copyFrom)
        {
            BaseTitle = copyFrom.BaseTitle;
            BaseAniDBID = copyFrom.BaseAniDBID;
            AniDBID = copyFrom.AniDBID;
            Season = copyFrom.Season;
            SceneSeason = copyFrom.SceneSeason;
            SearchTitle = copyFrom.SearchTitle;
            Episode = copyFrom.Episode;
        }

        public override object Clone()
        {
            return new AnimeReleaseInfo(this);
        }

        public override string ToString()
        {
            return string.Format("[ReleaseInfo: Title={0}, Guid={1}, Link={2}, Comments={3}, PublishDate={4}, Category={5}, Size={6}, Files={7}, Grabs={8}, Description={9}, RageID={10}, TVDBId={11}, Imdb={12}, TMDb={13}, Seeders={14}, Peers={15}, BannerUrl={16}, InfoHash={17}, MagnetUri={18}, MinimumRatio={19}, MinimumSeedTime={20}, DownloadVolumeFactor={21}, UploadVolumeFactor={22}, Gain={23}, BaseAniDBID={24}, AniDBID={25}]", Title, Guid, Link, Comments, PublishDate, Category, Size, Files, Grabs, Description, RageID, TVDBId, Imdb, TMDb, Seeders, Peers, BannerUrl, InfoHash, MagnetUri, MinimumRatio, MinimumSeedTime, DownloadVolumeFactor, UploadVolumeFactor, Gain, BaseAniDBID, AniDBID);
        }
    }
}
