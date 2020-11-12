﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiscordBot.Models;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using static Google.Apis.Sheets.v4.SpreadsheetsResource.ValuesResource;

namespace DiscordBot.DataAccess
{
    public interface IEventsSheetsService
    {
        Task AddEventAsync(string name, string description, DateTime time);
        Task EditEventAsync(
            int eventKey,
            string? description = null,
            string? name = null,
            DateTime? time = null
        );

        Task<DiscordEvent> GetEventAsync(int eventKey);
        Task<IEnumerable<DiscordEvent>> ListEventsAsync();
    }

    public class EventsSheetsService : IEventsSheetsService
    {
        private static readonly string[] scopes = { SheetsService.Scope.Spreadsheets };
        private static readonly string applicationName = "Softwire Discord Bot";
        private static readonly string spreadsheetId = "";

        private readonly SheetsService sheetsService;
        private int largestKey;

        private const string KeyColumn = "A";
        private const string NameColumn = "B";
        private const string DescriptionColumn = "C";
        private const string TimeColumn = "D";
        private const string LocationColumn = "E";

        public EventsSheetsService()
        {
            var credential = GetCredential();

            sheetsService = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = applicationName
            });

            largestKey = GetLargestKey();
        }

        public async Task ReadColumns()
        {
            var range = "A2:B27";
            var request =
                sheetsService.Spreadsheets.Values.Get(spreadsheetId, range);

            var response = await request.ExecuteAsync();
            var values = response.Values;
            if (values == null || values.Count <= 0)
            {
                Console.WriteLine("No data found.");
                return;
            }

            Console.WriteLine("Alpha, Numeric");
            foreach (var row in values)
            {
                Console.WriteLine($"{row[0]}, {row[1]}");
            }
        }

        public async Task WriteRow()
        {
            var valueRange = new ValueRange
            {
                Values = new List<IList<object>>
                {
                    new List<object>
                    {
                        "Test write:",
                        1
                    }
                },
                Range = "A30:B30"
            };
            var request = sheetsService.Spreadsheets.Values.Update(valueRange, spreadsheetId, "A30:B30");
            request.ValueInputOption = UpdateRequest.ValueInputOptionEnum.RAW;

            await request.ExecuteAsync();
        }

        public async Task AddEventAsync(string name, string description, DateTime time)
        {
            // Allocate new key
            largestKey++;

            var newRow = new ValueRange
            {
                Values = new IList<object>[]
                {
                    new object[]
                    {
                        largestKey,
                        name,
                        description,
                        time.ToString("s"),
                        "Europe/London"
                    }
                }
            };

            var request = sheetsService.Spreadsheets.Values.Append(
                newRow,
                spreadsheetId,
                $"{KeyColumn}:{LocationColumn}"
            );
            request.ValueInputOption = AppendRequest.ValueInputOptionEnum.RAW;

            await request.ExecuteAsync();
        }

        public async Task EditEventAsync(
            int eventKey,
            string? description = null,
            string? name = null,
            DateTime? time = null
        )
        {
            var rowNumber = await GetEventRowNumber(eventKey);

            var data = new List<ValueRange>();

            if (name != null)
            {
                data.Add(MakeCellUpdate($"EventsMetadata!{NameColumn}{rowNumber}", name));
            }

            if (description != null)
            {
                data.Add(MakeCellUpdate($"EventsMetadata!{DescriptionColumn}{rowNumber}", description));
            }

            if (time != null)
            {
                data.Add(MakeCellUpdate(
                    $"EventsMetadata!{TimeColumn}{rowNumber}",
                    time.Value.ToString("s")
                ));
            }

            if (!data.Any())
            {
                return;
            }

            var updateRequest = new BatchUpdateValuesRequest()
            {
                ValueInputOption = "RAW",
                Data = data
            };

            var request = sheetsService.Spreadsheets.Values.BatchUpdate(updateRequest, spreadsheetId);
            await request.ExecuteAsync();
        }

        public async Task RemoveEventAsync(int key)
        {
        }

        public async Task<DiscordEvent> GetEventAsync(int eventKey)
        {
             return new DiscordEvent("Christmas Day", "Christmas!", eventKey, new DateTime(2020, 12, 25));
        }

        public async Task<IEnumerable<DiscordEvent>> ListEventsAsync()
        {
            return new[]
            {
                new DiscordEvent("Christmas Day", "Christmas!", 1, new DateTime(2020, 12, 25))
            };
        }

        private static ServiceAccountCredential GetCredential(string path = "credentials.json")
        {
            using var stream =
                new FileStream(path, FileMode.Open, FileAccess.Read);

            return GoogleCredential.FromStream(stream)
                .CreateScoped(scopes)
                .UnderlyingCredential as ServiceAccountCredential;
        }

        private int GetLargestKey()
        {
            try
            {
                var request = sheetsService.Spreadsheets.Values.Get(
                    spreadsheetId,
                    $"EventsMetadata!{KeyColumn}:{KeyColumn}"
                );
                var response = request.Execute();

                if (response == null || response.Values.Count < 1)
                {
                    throw new EventsSheetsInitialisationException("Metadata sheet is empty");
                }

                // If the table only contains headers, and no data
                if (response.Values.Count == 1)
                {
                    return 0;
                }

                return int.Parse((string)response.Values.Skip(1).Last()[0]);
            }
            catch (GoogleApiException exception)
            {
                throw new EventsSheetsInitialisationException(
                    $"Events Sheets Service couldn't initialise",
                    exception
                );
            }
        }

        private async Task<int> GetEventRowNumber(int eventKey)
        {
            try
            {
                var request = sheetsService.Spreadsheets.Values.Get(
                    spreadsheetId,
                    $"EventsMetadata!{KeyColumn}:{KeyColumn}"
                );
                var response = await request.ExecuteAsync();

                if (response == null || response.Values.Count < 2)
                {
                    throw new EventNotFoundException($"Event key {eventKey} not recognised");
                }

                var rowNumber = response.Values
                    // Skip header row
                    .Skip(1)
                    // Parse the keys
                    .Select((rowValues, rowIndex) =>
                        (key: int.Parse((string)rowValues[0]), index: rowIndex)
                    )
                    .Where(row => row.key == eventKey)
                    // Extract row number, plus 2 to correct for two this:
                    // These lists are 0 indexed, but Sheets index from 1
                    // Correct for skipping row 1, the header
                    .Select(row => row.index + 2)
                    .Cast<int?>()
                    .FirstOrDefault();

                if (rowNumber == null)
                {
                    throw new EventNotFoundException($"Event key {eventKey} not recognised");
                }

                return rowNumber.Value;
            }
            catch (GoogleApiException exception)
            {
                throw new EventsSheetsInitialisationException(
                    "Events Sheets Service couldn't initialise",
                    exception
                );
            }

        }

        private ValueRange MakeCellUpdate(string range, object value)
        {
            return new ValueRange()
            {
                Range = range,
                Values = new IList<object>[] { new[] { value } }
            };
        }
    }
}
