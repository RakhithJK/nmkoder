﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Nmkoder.Data;
using Nmkoder.IO;

namespace Nmkoder.Media
{
    class GetFrameCountCached
    {
        public static Dictionary<QueryInfo, int> cache = new Dictionary<QueryInfo, int>();

        public static async Task<int> GetFrameCountAsync(string path)
        {
            Logger.Log($"Getting frame count ({path})", true);

            long filesize = IoUtils.GetFilesize(path);
            QueryInfo hash = new QueryInfo(path, filesize);

            if (filesize > 0 && CacheContains(hash))
            {
                Logger.Log($"Cache contains this hash, using cached value.", true);
                return GetFromCache(hash);
            }
            else
            {
                Logger.Log($"Hash not cached, reading frame count.", true);
            }

            int frameCount;

            if (IoUtils.IsPathDirectory(path))
                frameCount = IoUtils.GetAmountOfFiles(path, false);
            else
                frameCount = await FfmpegCommands.GetFrameCountAsync(path);

            Logger.Log($"Adding hash with value {frameCount} to cache.", true);
            cache.Add(hash, frameCount);

            return frameCount;
        }

        private static bool CacheContains (QueryInfo hash)
        {
            foreach(KeyValuePair<QueryInfo, int> entry in cache)
                if (entry.Key.path == hash.path && entry.Key.filesize == hash.filesize)
                    return true;

            return false;
        }

        private static int GetFromCache(QueryInfo hash)
        {
            foreach (KeyValuePair<QueryInfo, int> entry in cache)
                if (entry.Key.path == hash.path && entry.Key.filesize == hash.filesize)
                    return entry.Value;

            return 0;
        }

        public static void ClearCache()
        {
            cache.Clear();
        }
    }
}
