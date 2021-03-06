﻿using Ewah;
using Shared;
using StackOverflowTagServer.DataStructures;
using StackOverflowTagServer.Querying;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;

using HashSet = StackOverflowTagServer.CLR.HashSet<string>;
using NGrams = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>;
using TagLookup = System.Collections.Generic.Dictionary<string, int>;

// ReSharper disable LocalizableElement
namespace StackOverflowTagServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger.LogStartupMessage("IsServerGC: {0}, LatencyMode: {1}", GCSettings.IsServerGC, GCSettings.LatencyMode);

            var folder = @"C:\Users\warma11\Downloads\__GitHub__\StackOverflowTagServer\BinaryData\";
            var filename = @"Questions-NEW.bin";

            var startupTimer = Stopwatch.StartNew();
            var rawQuestions = TagServer.GetRawQuestionsFromDisk(folder, filename);
            //TagServer tagServer = TagServer.CreateFromScratchAndSaveToDisk(rawQuestions, intermediateFilesFolder: folder);
            TagServer tagServer = TagServer.CreateFromSerialisedData(rawQuestions, intermediateFilesFolder: folder); //, deserialiseBitMapsIndexes: false);
            startupTimer.Stop();

            GC.Collect(2, GCCollectionMode.Forced);
            var totalMemory = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
            Logger.LogStartupMessage("Took {0} ({1,6:N2} ms), in total to complete Startup - Using {2:N2} MB ({3:N2} GB) of memory in TOTAL",
                              startupTimer.Elapsed, startupTimer.Elapsed.TotalMilliseconds, totalMemory, totalMemory / 1024.0);

            //PrintQuestionStats(rawQuestions);
            //PrintTagStats(tagServer.AllTags);


            // Run a sanity check on all the query type, for the given exclusion list and nGrams
            var leppieTags = Utils.GetLeppieTagsFromResource();
            NGrams nGrams = WildcardProcessor.CreateNGrams(tagServer.AllTags);
            var expandedTags = WildcardProcessor.ExpandTagsNGrams(tagServer.AllTags, leppieTags, nGrams, printLoggingMessages: true);
            foreach (QueryType type in (QueryType[])Enum.GetValues(typeof(QueryType)))
            {
                var bitMap = tagServer.CreateBitMapIndexForExcludedTags(expandedTags, type, printLoggingMessages: true);
                tagServer.ValidateExclusionBitMap(bitMap, expandedTags, type);
                TestBitMapIndexQueries(tagServer, expandedTags, bitMap, type);
                RunComparisonQueries(tagServer, expandedTags, bitMap, type);
            }

            return;

#if false // code that currently isn't run, because of the early "return" statement above
            foreach (QueryType type in (QueryType[])Enum.GetValues(typeof(QueryType)))
            {
                using (Utils.SetConsoleColour(ConsoleColor.Green))
                    Logger.Log("Creating Bit Map index for \"{0}\"", type);
                //var expandedTags = WildcardProcessor.ExpandTagsNGrams(tagServer.AllTags, leppieTags, nGrams, printLoggingMessages: true);
                //var expandedTags = WildcardProcessor.ExpandTagsNGrams(tagServer.AllTags, new List<string>(new[] { "*c#*" }), nGrams, printLoggingMessages: true);
                var expandedTags = WildcardProcessor.ExpandTagsNGrams(tagServer.AllTags, new List<string>(new[] { "*c#*", "*java*" }), nGrams, printLoggingMessages: true);
                var bitMap = tagServer.CreateBitMapIndexForExcludedTags(expandedTags, type, printLoggingMessages: true);
                //using (Utils.SetConsoleColour(ConsoleColor.DarkGreen))
                //    Logger.Log(bitMap.ToDebugString(printEveryLiteralWord: false));
                //tagServer.ValidateExclusionBitMap(bitMap, expandedTags, type);
            }
            return;

            var expandedTagsNGrams = WildcardProcessor.ExpandTagsNGrams(tagServer.AllTags, leppieTags, nGrams, printLoggingMessages: true);
            //var expandedTagsNGrams = WildcardProcessor.ExpandTagsNGrams(tagServer.AllTags, new List<string>(new[] { "*c#*" }), nGrams, printLoggingMessages: true);
            var queryTypeToTest = QueryType.AnswerCount;
            var bitMapIndex = tagServer.CreateBitMapIndexForExcludedTags(expandedTagsNGrams, queryTypeToTest, printLoggingMessages: true);

            tagServer.ValidateExclusionBitMap(bitMapIndex, expandedTagsNGrams, queryTypeToTest);

            TestBitMapIndexQueries(tagServer, expandedTagsNGrams, bitMapIndex, queryTypeToTest);

            // Get some interesting stats on Leppie's Tag (how many qu's the cover/exclude, etc)
            //GetLeppieTagInfo(rawQuestions, tagServer.AllTags, leppieTags, leppieExpandedTags);

            return;

            // TODO currently it takes too long to create the Bit Map Index (expanding the wildcards to tag is fast though)
            // Either the Bit Map has to be cached OR we need to find a faster way of populating it
            TestWildcards(tagServer, nGrams, "*c#*");
            //TestWildcards(tagServer, nGrams, "*c#", "c#*");
            //TestWildcards(tagServer, nGrams, "*c#");
            //TestWildcards(tagServer, nGrams, "c#*");
            //TestWildcards(tagServer, nGrams, "c#-2.0");
            TestWildcards(tagServer, nGrams, leppieTags.ToArray());
            //TestWildcards(tagServer, nGrams, "*"); // INCLUDE all Tags

            //RunExclusionQueryTests(tagServer, leppieExpandedTags, runsPerLoop: 10);

            //RunSimpleQueries(tagServer);

            //Logger.LogStartupMessage("Finished, press <ENTER> to exit");
            //Console.ReadLine();
#endif
        }

        private static void GetLeppieTagInfo(List<Question> rawQuestions, TagLookup allTags, List<string> leppieTags, HashSet leppieExpandedTags)
        {
            Logger.Log("\nThere are {0:N0} questions and {1:N0} tags in total", rawQuestions.Count, allTags.Count);
            Logger.Log("Leppie list of {0:N0} tags contains {1:N0} that are wildcards", leppieTags.Count, leppieTags.Count(t => t.Contains('*')));
            Logger.Log("Leppie {0:N0} tags with wildcards expand to {1:N0} tags in total", leppieTags.Count, leppieExpandedTags.Count);
            var remainingTagsHashSet = new CLR.HashSet<string>(allTags.Keys);
            remainingTagsHashSet.ExceptWith(leppieExpandedTags);
            Logger.LogStartupMessage("There are {0:N0} tags remaining, {0:N0} + {1:N0} = {2:N0} (Expected: {3:N0})",
                              remainingTagsHashSet.Count, leppieExpandedTags.Count,
                              remainingTagsHashSet.Count + leppieExpandedTags.Count, allTags.Count);

            Logger.LogStartupMessage("Sanity checking excluded/included tags and questions...");
            var excludedQuestionCounter = rawQuestions.Count(question => question.Tags.Any(t => leppieExpandedTags.Contains(t)));
            var includedQuestionCounter = rawQuestions.Count(question => question.Tags.All(t => remainingTagsHashSet.Contains(t)));
            Logger.Log("{0:N0} EXCLUDED tags cover {1:N0} questions (out of {2:N0})",
                              leppieExpandedTags.Count, excludedQuestionCounter, rawQuestions.Count);
            Logger.Log(
                "{0:N0} remaining tags cover {1:N0} questions, {2:N0} + {3:N0} = {4:N0} (Expected: {5:N0})",
                remainingTagsHashSet.Count, includedQuestionCounter,
                includedQuestionCounter, excludedQuestionCounter,
                includedQuestionCounter + excludedQuestionCounter, rawQuestions.Count);
            Logger.Log();
        }

        private static HashSet ProcessTagsForFastLookup(TagLookup allTags, Trie<int> trie, NGrams nGrams, List<string> tagsToExpand)
        {
            var expandTagsContainsTimer = Stopwatch.StartNew();
            var expandTagsContains = WildcardProcessor.ExpandTagsContainsStartsWithEndsWith(allTags, tagsToExpand);
            expandTagsContainsTimer.Stop();

            //var expandTagsVBTimer = Stopwatch.StartNew();
            //var expandedTagsVB = WildcardProcessor.ExpandTagsVisualBasic(allTags, tagsToExpand);
            //expandTagsVBTimer.Stop();

            var expandTagsRegexTimer = Stopwatch.StartNew();
            var expandedTagsRegex = WildcardProcessor.ExpandTagsRegex(allTags, tagsToExpand);
            expandTagsRegexTimer.Stop();

            var expandTagsTrieTimer = Stopwatch.StartNew();
            var expandedTagsTrie = WildcardProcessor.ExpandTagsTrie(allTags, tagsToExpand, trie);
            expandTagsTrieTimer.Stop();

            var expandedTagsNGramsTimer = Stopwatch.StartNew();
            var expandedTagsNGrams = WildcardProcessor.ExpandTagsNGrams(allTags, tagsToExpand, nGrams, printLoggingMessages: true);
            expandTagsRegexTimer.Stop();

            Logger.LogStartupMessage("\nThere are {0:N0} tags in total", allTags.Count);
            Logger.LogStartupMessage("There are {0:N0} tags/wildcards (raw) BEFORE expansion", tagsToExpand.Count);

            Logger.LogStartupMessage("\nExpanded to {0,4:N0} tags (Contains),  took {1,8:N2} ms ({2})",
                                     expandTagsContains.Count, expandTagsContainsTimer.Elapsed.TotalMilliseconds, expandTagsContainsTimer.Elapsed);
            //Logger.LogStartupMessage("Expanded to {0,4:N0} tags (VB),        took {1,8:N2} ms ({2})",
            //                         expandedTagsVB.Count, expandTagsVBTimer.Elapsed.TotalMilliseconds, expandTagsVBTimer.Elapsed);
            Logger.LogStartupMessage("Expanded to {0,4:N0} tags (Regex),     took {1,8:N2} ms ({2})",
                                     expandedTagsRegex.Count, expandTagsRegexTimer.Elapsed.TotalMilliseconds, expandTagsRegexTimer.Elapsed);
            Logger.LogStartupMessage("Expanded to {0,4:N0} tags (Trie),      took {1,8:N2} ms ({2})",
                                     expandedTagsTrie.Count, expandTagsTrieTimer.Elapsed.TotalMilliseconds, expandTagsTrieTimer.Elapsed);
            Logger.LogStartupMessage("Expanded to {0,4:N0} tags (N-Grams),   took {1,8:N2} ms ({2})",
                                     expandedTagsNGrams.Count, expandedTagsNGramsTimer.Elapsed.TotalMilliseconds, expandedTagsNGramsTimer.Elapsed);

            Logger.LogStartupMessage("\nIn Contains but not in Regex: " + string.Join(", ", expandTagsContains.Except(expandedTagsRegex)));
            Logger.LogStartupMessage("\nIn Regex but not in Contains: " + string.Join(", ", expandedTagsRegex.Except(expandTagsContains)));

            //Logger.LogStartupMessage("\nIn Contains but not in VB: " + string.Join(", ", expandTagsContains.Except(expandedTagsVB)));
            //Logger.LogStartupMessage("\nIn VB but not in Contains: " + string.Join(", ", expandedTagsVB.Except(expandTagsContains)));

            Logger.LogStartupMessage("\nIn Contains but not in Trie: " + string.Join(", ", expandTagsContains.Except(expandedTagsTrie)));
            Logger.LogStartupMessage("\nIn Trie but not in Contains: " + string.Join(", ", expandedTagsTrie.Except(expandTagsContains)));

            Logger.LogStartupMessage("\nIn Contains but not in NGrams: " + string.Join(", ", expandTagsContains.Except(expandedTagsNGrams)));
            Logger.LogStartupMessage("\nIn NGrams but not in Contains: " + string.Join(", ", expandedTagsNGrams.Except(expandTagsContains)));
            Logger.LogStartupMessage();

            var expandedTags = expandedTagsNGrams;
            //Logger.LogStartupMessage(string.Join(", ", expandedTags));

            // This is an error, we shouldn't have extra tags that aren't in the "allTags" list!!
            var extra = expandedTags.Except(allTags.Keys).ToList();
            if (extra.Count > 0)
                Logger.LogStartupMessage("\nExtra Tags: " + string.Join(", ", extra) + "\n");

            return expandedTags;
        }

        private static void TestWildcards(TagServer tagServer, NGrams nGrams, params string[] tagsToExpandInput)
        {
            var tagsToExpand = tagsToExpandInput.ToList();

            if (tagsToExpand.Count == 1 && tagsToExpand[0] == "*")
            {
                // special case!!
                using (Utils.SetConsoleColour(ConsoleColor.Green))
                    Logger.Log("\nTestWildcards: special case, using ALL Tags", String.Join(", ", tagsToExpand));
                var bitMapIndex = tagServer.CreateBitMapIndexForExcludedTags(new CLR.HashSet<string>(tagServer.AllTags.Keys), QueryType.AnswerCount, printLoggingMessages: true);
                return;
            }

            using (Utils.SetConsoleColour(ConsoleColor.Green))
                Logger.Log("\nTestWildcards: {0}\n", String.Join(", ", tagsToExpand.Where(t => t.Contains('*'))));

            var timer = Stopwatch.StartNew();
            var expandedTagsNGrams = WildcardProcessor.ExpandTagsNGrams(tagServer.AllTags, tagsToExpand, nGrams, printLoggingMessages: true);
            timer.Stop();
            using (Utils.SetConsoleColour(ConsoleColor.DarkYellow))
            {
                Logger.LogStartupMessage("Took {0,6:N2} ms ({1}) to expanded Wildcards to {2,2:N0} tags (using N-Grams, with N={3})",
                                         timer.Elapsed.TotalMilliseconds, timer.Elapsed, expandedTagsNGrams.Count, WildcardProcessor.N);
            }

            var wildcards = tagsToExpand.Where(t => t.Contains('*')).ToList();
            Logger.LogStartupMessage("There are {0:N0} wildcards in the list and {1:N0} regular tags (i.e. with no '*' in them)",
                                     wildcards.Count, tagsToExpand.Count(w => w.Contains('*') == false));
            if (wildcards.Count > 50)
                Logger.LogStartupMessage("Wildcards: TOO MANY TO PRINT (there are {0:N0} wildcards)", wildcards.Count);
            else
                Logger.LogStartupMessage("Wildcards: [{0}]", String.Join(", ", tagsToExpand.Where(w => w.Contains('*'))));

            var expansions = tagsToExpand.Where(w => w.Contains('*'))
                                      .Select(w => String.Format("{0} -> {1}", w, String.Join(", ", WildcardProcessor.CreateSearches(w))))
                                      .ToList();
            if (expansions.Count > 50)
                Logger.LogStartupMessage("Expansions: TOO MANY TO PRINT (there are {0:N0} expansions)", expansions.Count);
            else
                Logger.LogStartupMessage("Expansions:\n  {0}", String.Join("\n  ", expansions));

            if (expandedTagsNGrams.Count > 50)
                Logger.LogStartupMessage("Results: TOO MANY TO PRINT (there are {0:N0} results)", expandedTagsNGrams.Count);
            else
                Logger.LogStartupMessage("Results: [{0}]", String.Join(", ", expandedTagsNGrams));

            var expandTagsContainsTimer = Stopwatch.StartNew();
            var expandTagsContains = WildcardProcessor.ExpandTagsContainsStartsWithEndsWith(tagServer.AllTags, tagsToExpand);
            expandTagsContainsTimer.Stop();

            Logger.LogStartupMessage("\nIn Contains but not in NGrams: " + string.Join(", ", expandTagsContains.Except(expandedTagsNGrams)));
            Logger.LogStartupMessage("\nIn NGrams but not in Contains: " + string.Join(", ", expandedTagsNGrams.Except(expandTagsContains)));
            Logger.LogStartupMessage();

            var bitMapIndexAnswerCount = tagServer.CreateBitMapIndexForExcludedTags(expandedTagsNGrams, QueryType.AnswerCount, printLoggingMessages: true);
            //var bitMapIndexCreationDate = tagServer.CreateBitMapIndexForExcludedTags(expandedTagsNGrams, QueryType.CreationDate, printLoggingMessages: true);
            //var bitMapIndexLastActivityDate = tagServer.CreateBitMapIndexForExcludedTags(expandedTagsNGrams, QueryType.LastActivityDate, printLoggingMessages: true);
            //var bitMapIndexScore = tagServer.CreateBitMapIndexForExcludedTags(expandedTagsNGrams, QueryType.Score, printLoggingMessages: true);
            //var bitMapIndexViewCount = tagServer.CreateBitMapIndexForExcludedTags(expandedTagsNGrams, QueryType.ViewCount, printLoggingMessages: true);
        }

        private static void TestBitMapIndexQueries(TagServer tagServer, CLR.HashSet<string> tagsToExclude, EwahCompressedBitArray exclusionBitMapIndex, QueryType queryTypeToTest)
        {
            foreach (var @operator in new[] { "OR", "OR-NOT", "AND", "AND-NOT" })
            {
                var tagsPairings = new[]
                {
                    Tuple.Create("c#", "java"),
                    Tuple.Create("c#", "jquery"),
                    Tuple.Create("c#", "javascript"),
                    Tuple.Create("c#", ".net-3.5"), // large -> small
                    Tuple.Create(".net-3.5", "c#"), // small -> large
                };

                // Run queries WITHOUT exclusion Bit Map Index
                using (Utils.SetConsoleColour(ConsoleColor.Green))
                    Logger.Log("Running \"{0}\" Queries", @operator);

                foreach (var pairing in tagsPairings)
                {
                    TestBitMapIndexAndValidateResults(
                            tagServer,
                            new QueryInfo { Tag = pairing.Item1, OtherTag = pairing.Item2, Type = queryTypeToTest, Operator = @operator });
                }

                // Run queries WITH exclusion Bit Map Index
                using (Utils.SetConsoleColour(ConsoleColor.Green))
                    Logger.Log("Running \"{0}\" Queries and using an Exclusion Bit Map Index", @operator);

                foreach (var pairing in tagsPairings)
                {
                    TestBitMapIndexAndValidateResults(
                            tagServer,
                            new QueryInfo { Tag = pairing.Item1, OtherTag = pairing.Item2, Type = queryTypeToTest, Operator = @operator },
                            tagsToExclude: tagsToExclude,
                            exclusionBitMap: exclusionBitMapIndex);
                }
            }
        }

        private static void TestBitMapIndexAndValidateResults(TagServer tagServer, QueryInfo queryInfo,
                                                              CLR.HashSet<string> tagsToExclude = null,
                                                              EwahCompressedBitArray exclusionBitMap = null)
        {
            var result = tagServer.ComparisionQueryBitMapIndex(queryInfo, exclusionBitMap, printLoggingMessages: true);
            var errors = tagServer.GetInvalidResults(result.Questions, queryInfo);
            if (errors.Any())
            {
                using (Utils.SetConsoleColour(ConsoleColor.Red))
                    Logger.Log("ERROR Running \"{0}\" Query, {1} (out of {2}) results were invalid",
                               queryInfo.Operator, errors.Count, result.Questions.Count);
                foreach (var qu in errors)
                {
                    Logger.Log("  {0,8}: {1}", qu.Id, String.Join(", ", qu.Tags));
                }
                Logger.Log();
            }

            if (tagsToExclude != null && exclusionBitMap != null)
            {
                var shouldHaveBeenExcluded = tagServer.GetShouldHaveBeenExcludedResults(result.Questions, queryInfo, tagsToExclude);
                if (shouldHaveBeenExcluded.Any())
                {
                    using (Utils.SetConsoleColour(ConsoleColor.Red))
                        Logger.Log("ERROR Running \"{0}\" Query, {1} (out of {2}) questions should have been excluded",
                                   queryInfo.Operator, shouldHaveBeenExcluded.Select(s => s.Item1.Id).Distinct().Count(), result.Questions.Count);
                    foreach (var error in shouldHaveBeenExcluded)
                    {
                        Logger.Log("  {0,8}: {1} -> {2}", error.Item1.Id, String.Join(", ", error.Item1.Tags), string.Join(", ", error.Item2));
                    }
                    Logger.Log();
                }
            }
        }

        private static void RunComparisonQueries(TagServer tagServer, CLR.HashSet<string> tagsToExclude, EwahCompressedBitArray exclusionBitMap, QueryType queryTypeToTest)
        {
            var smallTag = tagServer.AllTags.Where(t => t.Value <= 200).First().Key;
            string largeTag = ".net";
            int pageSize = 25;

            // LARGE 1st Tag, SMALL 2nd Tag
            //RunAndOrNotComparisionQueries(tagServer, tag1: largeTag, tag2: smallTag, pageSize: pageSize);
            // SMALL 1st Tag, LARGE 2nd Tag
            //RunAndOrNotComparisionQueries(tagServer, tag1: smallTag, tag2: largeTag, pageSize: pageSize);

            // 2 large tags (probably the worst case)
            //RunAndOrNotComparisionQueries(tagServer, "c#", "jquery", pageSize);
            //RunAndOrNotComparisionQueries(tagServer, ".net", "jquery", pageSize);
            // Now run the same tests, but with "Exclusions" applied to the queries
            RunAndOrNotComparisionQueries(tagServer, ".net", "jquery", pageSize, queryTypeToTest, tagsToExclude, exclusionBitMap);
        }


        private static void RunAndOrNotComparisionQueries(TagServer tagServer, string tag1, string tag2, int pageSize, QueryType queryTypeToTest,
                                                          CLR.HashSet<string> tagsToExclude = null, EwahCompressedBitArray exclusionBitMap = null)
        {
            using (Utils.SetConsoleColour(ConsoleColor.Green))
                Logger.LogStartupMessage("\nComparison queries:\n\t\"{0}\" has {1:N0} questions\n\t\"{2}\" has {3:N0} questions",
                                         tag1, tagServer.AllTags[tag1], tag2, tagServer.AllTags[tag2]);

            var queries = new[] { "AND", "OR", "AND-NOT", "OR-NOT" };
            var skipCounts = new[] { 0, 100, 250, 500, 1000, 2000, 4000, 8000 };
            foreach (var query in queries)
            {
                Results.CreateNewFile(string.Format("Results-{0}{1}-{2}-{3}-{4}-{5}.csv",
                                                    (tagsToExclude != null && exclusionBitMap != null) ? "With-Exclusions-" : "",
                                                    DateTime.Now.ToString("yyyy-MM-dd @ HH-mm-ss"), tag1, query, tag2, queryTypeToTest));
                Results.AddHeaders("Skip Count",
                                   String.Format("Regular {0} {1} {2}", tag1, query, tag2),
                                   String.Format("LINQ {0} {1} {2}", tag1, query, tag2),
                                   String.Format("BitMap {0} {1} {2}", tag1, query, tag2),
                                   String.Format("Regular {0} {1} {2}", tag2, query, tag1),
                                   String.Format("LINQ {0} {1} {2}", tag2, query, tag1),
                                   String.Format("BitMap {0} {1} {2}", tag2, query, tag1));

                using (Utils.SetConsoleColour(ConsoleColor.Yellow))
                    Logger.LogStartupMessage("\n{0} Comparison queries: {1} {0} {2}\n", query, tag1, tag2);
                foreach (var skipCount in skipCounts)
                {
                    Results.AddData(skipCount.ToString());

                    // Run the query both ways round, i.e. "c# AND-NOT jquery" as well as "jquery AND-NOT c#")
                    foreach (var tagPair in new[] { Tuple.Create(tag1, tag2), Tuple.Create(tag2, tag1) })
                    {
                        var info = new QueryInfo
                        {
                            Type = queryTypeToTest,
                            Tag = tagPair.Item1,
                            OtherTag = tagPair.Item2,
                            Operator = query,
                            PageSize = pageSize,
                            Skip = skipCount
                        };

                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        var resultRegular = tagServer.ComparisonQueryNoLINQ(info, tagsToExclude);
                        var resultLINQ = tagServer.ComparisonQuery(info, tagsToExclude);

                        //using (Utils.SetConsoleColour(ConsoleColor.Gray));
                        //    Utils.CompareLists(resultRegular.Questions, "Regular", resultLINQ.Questions, "LINQ");

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        var resultBitMap = tagServer.ComparisionQueryBitMapIndex(info, exclusionBitMap, printLoggingMessages: true);

                        var invalidResults = tagServer.GetInvalidResults(resultBitMap.Questions, info);
                        var shouldHaveBeenExcludedResults = tagServer.GetShouldHaveBeenExcludedResults(resultBitMap.Questions, info, tagsToExclude);
                        if (shouldHaveBeenExcludedResults.Count > 0)
                        {
                            using (Utils.SetConsoleColour(ConsoleColor.Red))
                                Logger.LogStartupMessage("ERROR: shouldHaveBeenExcludedResults contains {0} items", shouldHaveBeenExcludedResults.Count);
                        }

                        using (Utils.SetConsoleColour(ConsoleColor.Red))
                        {
                            // See the TODO comments in ComplexQueryProcessor.cs for an explanation of this issue
                            if (query == "OR" || query == "OR-NOT")
                                Logger.LogStartupMessage("It is EXPECTED that {0} queries won't match when comparing \"Regular\" v. \"BitMap\"", query);
                        }

                        //using (Utils.SetConsoleColour(ConsoleColor.Gray));
                        //    Utils.CompareLists(resultRegular.Questions, "Regular", resultBitMap.Questions, "BitMap");
                    }

                    Console.ResetColor();
                    Results.StartNewRow();
                }

                Results.CloseFile();
            }
        }

        private static void RunExclusionQueryTests(TagServer tagServer, HashSet expandedTags, int runsPerLoop)
        {
            Results.CreateNewFile(string.Format("Results-Exclusion-Queries-{0}.csv", DateTime.Now.ToString("yyyy-MM-dd @ HH-mm-ss")));
            //Results.AddHeaders("Count", "Slow", "Fast", "Fast Alt"); // "Bloom"
            Results.AddHeaders("Count", "Fast", "Fast Alt");

            var amounts = new List<int>();
            for (decimal notQueries = (25.0m / 16.0m); notQueries <= 6400; notQueries *= 2)
            {
                amounts.Add((int)notQueries);
            }
            amounts.Add(expandedTags.Count);

            Logger.LogStartupMessage("\n\nVarying the number of \"Not Queries\"\n");
            var expandedTagsAsList = expandedTags.ToList();
            //while (true)
            {
                foreach (var count in amounts)
                {
                    var pageSize = 50;
                    var excludedTags = Utils.SelectNItemsFromList(expandedTagsAsList, count);
                    //Logger.LogStartupMessage("Count={0}, excludedTags.Count={1} tags:{2}\n\n", count, excludedTags.Count, string.Join(", ", excludedTags));
                    //continue;

                    //GC.Collect(2, GCCollectionMode.Forced);
                    for (int i = 0; i < runsPerLoop; i++)
                    {
                        Results.AddData(excludedTags.Count.ToString());

                        //var results1 = tagServer.BooleanQueryWithExclusionsLINQVersion(QueryType.Score, ".net", excludedTags, pageSize: pageSize);
                        var results2 = tagServer.BooleanQueryWithExclusionsFastVersion(QueryType.Score, ".net", excludedTags, pageSize: pageSize);
                        var results3 = tagServer.BooleanQueryWithExclusionsFastAlternativeVersion(QueryType.Score, ".net", excludedTags, pageSize: pageSize);
                        //var results4 = tagServer.BooleanQueryWithExclusionsBloomFilterVersion(QueryType.Score, ".net", excludedTags, pageSize: pageSize);

                        //Utils.CompareLists(listA: results1, nameA: "Slow", listB: results2, nameB: "Fast");
                        Utils.CompareLists(listA: results2, nameA: "Fast", listB: results3, nameB: "FastAlternative");
                        //Utils.CompareLists(listA: results3, nameA: "FastAlternative", listB: results4, nameB: "Bloom");

                        Results.StartNewRow();
                    }
                    //GC.Collect(2, GCCollectionMode.Forced);
                }
            }

            //Logger.LogStartupMessage("\n\nVarying the number of \"Skipped Pages\"\n");
            ////for (decimal skip = (25.0m / 16.0m); skip <= 1600; skip *= 2)
            //for (decimal skip = (25.0m / 16.0m); skip <= 800; skip *= 2)
            //{
            //    var pageSize = 50;
            //    for (int i = 0; i < runsPerLoop; i++)
            //    {
            //        var resultsSlow = tagServer.BooleanQueryWithExclusionsLINQVersion(QueryType.Score, ".net", notQueries: 400, pageSize: pageSize, skip: (int)skip);
            //        var resultsFast = tagServer.BooleanQueryWithExclusionsFastVersion(QueryType.Score, ".net", notQueries: 400, pageSize: pageSize, skip: (int)skip);
            //        var resultsFastAlt = tagServer.BooleanQueryWithExclusionsFastAlternativeVersion(QueryType.Score, ".net", notQueries: 400, pageSize: pageSize, skip: (int)skip);
            //        //tagServer.BooleanQueryWithExclusionsBloomFilterVersion(QueryType.Score, ".net", notQueries: 400, pageSize: pageSize, skip: (int)skip);
            //    }
            //}

            //return;

            Results.CloseFile();
        }

        private static void RunSimpleQueries(TagServer tagServer)
        {
            var queryTester = new QueryTester(tagServer.Questions);
            queryTester.TestAndOrNotQueries();
            queryTester.TestQueries();

            Logger.LogStartupMessage("Finished, press <ENTER> to exit");
            Console.ReadLine();
            return;

            //Regular queries, i.e.single tag, no Boolean operators
            tagServer.Query(QueryType.LastActivityDate, "c#", pageSize: 10, skip: 0);
            tagServer.Query(QueryType.LastActivityDate, "c#", pageSize: 10, skip: 9);
            tagServer.Query(QueryType.LastActivityDate, "c#", pageSize: 10, skip: 10);

            tagServer.Query(QueryType.LastActivityDate, "c#", pageSize: 100, skip: 10000);
            tagServer.Query(QueryType.LastActivityDate, "c#", pageSize: 100, skip: 1000000);

            tagServer.Query(QueryType.Score, ".net", pageSize: 6, skip: 95);
            tagServer.Query(QueryType.Score, ".net", pageSize: 6, skip: 100);
            tagServer.Query(QueryType.Score, ".net", pageSize: 6, skip: 105);
        }

        private static void PrintQuestionStats(List<Question> rawQuestions)
        {
            // For sanity checks!!
            Logger.LogStartupMessage("Max LastActivityDate {0}", rawQuestions.Max(q => q.LastActivityDate));
            Logger.LogStartupMessage("Min LastActivityDate {0}\n", rawQuestions.Min(q => q.LastActivityDate));

            Logger.LogStartupMessage("Max CreationDate {0}", rawQuestions.Max(q => q.CreationDate));
            Logger.LogStartupMessage("Min CreationDate {0}\n", rawQuestions.Min(q => q.CreationDate));

            Logger.LogStartupMessage("Max  Score {0}", rawQuestions.Max(q => q.Score));
            Logger.LogStartupMessage("Min  Score {0}", rawQuestions.Min(q => q.Score));
            Logger.LogStartupMessage("Null Score {0}\n", rawQuestions.Count(q => q.Score == null));

            Logger.LogStartupMessage("Max  ViewCount {0}", rawQuestions.Max(q => q.ViewCount));
            Logger.LogStartupMessage("Min  ViewCount {0}", rawQuestions.Min(q => q.ViewCount));
            Logger.LogStartupMessage("Null ViewCount {0}\n", rawQuestions.Count(q => q.ViewCount == null));

            Logger.LogStartupMessage("Max  AnswerCount {0}", rawQuestions.Max(q => q.AnswerCount));
            Logger.LogStartupMessage("Min  AnswerCount {0}", rawQuestions.Min(q => q.AnswerCount));
            Logger.LogStartupMessage("Null AnswerCount {0}\n", rawQuestions.Count(q => q.AnswerCount == null));

            var mostViewed = rawQuestions.OrderByDescending(q => q.ViewCount).First();
            var mostAnswers = rawQuestions.OrderByDescending(q => q.AnswerCount).First();
            var highestScore = rawQuestions.OrderByDescending(q => q.Score).First();
            Logger.LogStartupMessage("Max ViewCount {0}, Question Id {1}", mostViewed.ViewCount, mostViewed.Id);
            Logger.LogStartupMessage("Max Answers {0}, Question Id {1}", mostAnswers.AnswerCount, mostAnswers.Id);
            Logger.LogStartupMessage("Max Score {0}, Question Id {1}\n", highestScore.Score, highestScore.Id);
        }

        private static void PrintTagStats(TagLookup allTags)
        {
            var histogram = new Dictionary<int, List<KeyValuePair<string, int>>>();
            foreach (var tag in allTags)
            {
                if (tag.Key == TagServer.ALL_TAGS_KEY)
                    continue;

                var bucket = -1;
                if (tag.Value <= 10)
                    bucket = tag.Value;
                else if (tag.Value < 100)
                    bucket = ((tag.Value / 10) * 10) + 10;
                else if (tag.Value < 1000)
                    bucket = ((tag.Value / 100) * 100) + 100;
                else if (tag.Value < 10000)
                    bucket = ((tag.Value / 1000) * 1000) + 1000;
                else if (tag.Value < 100000)
                    bucket = ((tag.Value / 10000) * 10000) + 10000;
                else if (tag.Value < 1000000)
                    bucket = ((tag.Value / 100000) * 100000) + 100000;
                else
                    bucket = tag.Value;

                if (bucket == -1)
                {
                    Logger.LogStartupMessage("Error: ({0}, {1})", tag.Key, tag.Value);
                    continue;
                }

                if (histogram.ContainsKey(bucket))
                {
                    var bucketInfo = histogram[bucket];
                    bucketInfo.Add(tag);
                }
                else
                {
                    var list = new List<KeyValuePair<string, int>> { tag };
                    histogram.Add(bucket, list);
                }
            }

            var totalSoFar = 0;
            var totalsPerBucket = new Dictionary<int, long>();
            foreach (var bucket in histogram.OrderBy(h => h.Key))
            {
                totalSoFar += bucket.Value.Count;
                totalsPerBucket.Add(bucket.Key, totalSoFar);
            }

            Logger.LogStartupMessage();
            totalSoFar = 0;
            foreach (var bucket in histogram.OrderByDescending(h => h.Key))
            {
                totalSoFar += bucket.Value.Count;
                Logger.LogStartupMessage("{0,8:N0}: {1} {2} {3}",
                    bucket.Key,
                    bucket.Value.Count.ToString("N0").PadRight(8),
                    totalsPerBucket[bucket.Key].ToString("N0").PadRight(8),
                    totalSoFar.ToString("N0").PadRight(8));
            }
        }
    }
}
// ReSharper restore LocalizableElement
