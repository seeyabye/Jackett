using System.Collections.Generic;

namespace Jackett.Common.Models.IndexerConfig.Bespoke
{
    internal class ConfigurationDataAnimeBytes : ConfigurationDataUserPasskey
    {
        public BoolItem IncludeRaw { get; private set; }
        //public DisplayItem DateWarning { get; private set; }
        public BoolItem PadEpisode { get; private set; }
        public BoolItem AddSynonyms { get; private set; }
        public BoolItem FilterSeasonEpisode { get; private set; }
        public SelectItem AiringEpisode { get; private set; }
        public StringItem HardDriveCacheKeepTime { get; private set; }


        public ConfigurationDataAnimeBytes(string instructionMessageOptional = null)
            : base()
        {
            Dictionary<string, string> airingOptions = new Dictionary<string, string>();
            airingOptions.Add("-1", "---");
            airingOptions.Add("0", "Exclude");
            airingOptions.Add("1", "Only Airing");

            IncludeRaw = new BoolItem() { Name = "IncludeRaw", Value = false };
            //DateWarning = new DisplayItem("This tracker does not supply upload dates so they are based off year of release.") { Name = "DateWarning" };
            PadEpisode = new BoolItem() { Name = "Pad episode number for Sonarr compatability", Value = false };
            AddSynonyms = new BoolItem() { Name = "Add releases for each synonym title", Value = true };
            FilterSeasonEpisode = new BoolItem() { Name = "Filter results by season/episode", Value = false };
            AiringEpisode = new SelectItem(airingOptions) { Name = "Show Airing Episodes?", Value = "-1" };
            Instructions = new DisplayItem(instructionMessageOptional) { Name = "" };
            HardDriveCacheKeepTime = new StringItem { Name = "Keep Cached files for (ms)", Value = "86400000" }; // Store for one day
        }
    }
}
