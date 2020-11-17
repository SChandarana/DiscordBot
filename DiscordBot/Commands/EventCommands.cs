﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiscordBot.DataAccess;
using DiscordBot.DataAccess.Exceptions;
using DiscordBot.DataAccess.Models;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;

namespace DiscordBot.Commands
{
    [Group("event")]
    [RequireRoles(RoleCheckMode.All, "Bot Whisperer")]
    internal class EventCommands : BaseCommandModule
    {
        private static readonly string[] EventOperations =
        {
            "create",
            "list",
            "remove",
            "show",
            "edit",
            "start",
            "stop"
        };
        private static readonly string[] EventFields =
        {
            "name",
            "description",
            "time",
            "stop"
        };
        private static readonly string[] ConfirmationResponses =
        {
            "yes",
            "no"
        };

        private readonly IEventsSheetsService eventsSheetsService;

        public EventCommands(IEventsSheetsService eventsSheetsService)
        {
            this.eventsSheetsService = eventsSheetsService;
        }

        [GroupCommand]
        [Description("Initiates the wizard for event-related actions")]
        public async Task Event(CommandContext context)
        {
            await context.RespondAsync(
                $"{context.Member.Mention} - choose one of the actions below or answer ``stop`` to cancel. (time out in 30s)\n" +
                "``create`` - create new event.\n" +
                "``list`` - list events\n" +
                "``remove`` - delete event.\n" +
                "``show`` - show event details.\n" +
                "``edit`` - edit event.\n" +
                "``start`` - open signups for event."
            );

            var eventOperation = await GetUserResponse(context, EventOperations);
            if (eventOperation == null)
            {
                return;
            }

            switch (eventOperation)
            {
                case "create":
                    await CreateEventName(context);
                    break;
                case "list":
                    await ListEvents(context);
                    break;
                case "remove":
                    await RemoveEvent(context);
                    break;
                case "show":
                    await ShowEvent(context);
                    break;
                case "edit":
                    await EditEvent(context);
                    break;
                case "start":
                    await CreateSignupSheet(context);
                    break;
            }
        }

        [Command("create")]
        public async Task CreateEventName(CommandContext context)
        {
            await context.RespondAsync($"{context.Member.Mention} - what is the name of the event?");
            var eventName = await GetUserResponse(context);
            if (eventName == null)
            {
                return;
            }

            await CreateEventDescription(context, eventName);
        }

        [Command("create")]
        public async Task CreateEventDescription(CommandContext context, string eventName)
        {
            await context.RespondAsync($"{context.Member.Mention} - give an event description.");
            var eventDescription = await GetUserResponse(context);
            if (eventDescription == null)
            {
                return;
            }

            await CreateEventTime(context, eventName, eventDescription);
        }

        [Command("create")]
        public async Task CreateEventTime(CommandContext context, string eventName, string eventDescription)
        {
            await context.RespondAsync($"{context.Member.Mention} - what time is your event?");
            var eventTime = await GetUserTimeResponse(context);
            if (eventTime == null)
            {
                return;
            }

            await CreateEvent(context, eventName, eventDescription, eventTime.Value.DateTime);
        }

        [Command("create")] 
        public async Task CreateEvent(CommandContext context, string eventName, string eventDescription, DateTime eventTime)
        {
            await eventsSheetsService.AddEventAsync(eventName, eventDescription, eventTime);

            var eventEmbed = new DiscordEmbedBuilder
            {
                Title = eventName,
                Description = eventDescription,
                Color = new DiscordColor(0xFFFFFF),
                Timestamp = eventTime
            };

            await context.RespondAsync(embed: eventEmbed);
        }

        [Command("remove")]
        public async Task RemoveEvent(CommandContext context)
        {
            await context.RespondAsync($"{context.Member.Mention} - what is the event key? (use the ``list`` option to find out)");
            var eventKey = await GetUserIntResponse(context);
            if (eventKey == null)
            {
                return;
            }

            var discordEmbed = await GetEventEmbed(context, eventKey.Value);
            if (discordEmbed == null)
            {
                return;
            }

            await context.RespondAsync(
                $"{context.Member.Mention} - is this the event you want to delete? (``yes``/``no``)",
                embed: discordEmbed);
            var confirmationResponse = await GetUserConfirmation(context);
            if (confirmationResponse == null || confirmationResponse == false)
            {
                return;
            }

            try
            {
                await eventsSheetsService
                    .RemoveEventAsync(eventKey.Value);
                await context.RespondAsync($"{context.Member.Mention} - poof! It's gone.");
            }
            catch (EventNotFoundException)
            {
                await context.RespondAsync($"{context.Member.Mention} - operation stopped: event not found.");
            }
        }

        [Command("show")]
        public async Task ShowEvent(CommandContext context)
        {
            await context.RespondAsync($"{context.Member.Mention} - what is the event key? (use the ``list`` option to find out)");
            var eventKey = await GetUserIntResponse(context);
            if (eventKey == null)
            {
                return;
            }
            
            var eventEmbed = await GetEventEmbed(context, eventKey.Value);
            if (eventEmbed == null)
            {
                return;
            }
            
            await context.RespondAsync($"{context.Member.Mention} - here is the event", embed: eventEmbed);
        }
  
        public async Task ListEvents(CommandContext context)
        {
            var eventsList = await eventsSheetsService.ListEventsAsync();

            var eventsListEmbed = new DiscordEmbedBuilder
            {
                Title = "Events"
            };

            foreach (var discordEvent in eventsList)
            {
                eventsListEmbed.AddField(
                    $"{discordEvent.Key}) {discordEvent.Name}",
                    $"{discordEvent.Time}"
                );
            }

            await context.RespondAsync($"{context.Member.Mention} - here are all created events.", embed: eventsListEmbed);
        }

        public async Task EditEvent(CommandContext context)
        {
            await context.RespondAsync($"{context.Member.Mention} - what is the event key? (use the ``list`` option to find out)");
            var eventKey = await GetUserIntResponse(context);
            if (eventKey == null)
            {
                return;
            }

            var eventEmbed = await GetEventEmbed(context, eventKey.Value);
            if (eventEmbed == null)
            {
                return;
            }

            await context.RespondAsync($"{context.Member.Mention}", embed: eventEmbed);
            await context.RespondAsync(
                $"{context.Member.Mention} - what field do you want to edit? (``name``, ``description``, ``time``)\n");
            var editField = await GetUserResponse(context, EventFields);
            if (editField == null)
            {
                return;
            }

            await EditEventField(context, eventKey.Value, editField, eventEmbed);
        }

        private async Task CreateSignupSheet(CommandContext context)
        {
            await context.RespondAsync($"{context.Member.Mention} - what is the event key? (use the ``list`` option to find out)");
            var eventKey = await GetUserIntResponse(context);
            if (eventKey == null)
            {
                return;
            }

            var eventEmbed = await GetEventEmbed(context, eventKey.Value);
            if (eventEmbed == null)
            {
                return;
            }

            await context.RespondAsync($"{context.Member.Mention} - start signups for this event? - (``yes``/``no``)", embed: eventEmbed);
            var confirmationResponse = await GetUserConfirmation(context);
            if (confirmationResponse == null || confirmationResponse == false)
            {
                return;
            }

            await SendSignupMessage(context, eventKey.Value);
        }

        private async Task SendSignupMessage(CommandContext context, int eventKey)
        { 
            var discordEvent = await eventsSheetsService.GetEventAsync(eventKey);

            var signupsByResponse = await eventsSheetsService.GetSignupsByResponseAsync(eventKey);

            var signupEmbed = GetSignupEmbed(discordEvent, signupsByResponse);

            var signupMessage = await context.RespondAsync($"Signups are open for __**{discordEvent.Name}**__!", embed: signupEmbed);
            await eventsSheetsService.AddMessageIdToEventAsync(eventKey, signupMessage.Id);

            foreach (var response in signupsByResponse.Keys)
            {
                await signupMessage.CreateReactionAsync(DiscordEmoji.FromName(context.Client, response.Emoji));
            }
        }

        private Dictionary<ulong, IEnumerable<EventResponse>> GetResponsesByUser(
            Dictionary<EventResponse, IEnumerable<ulong>> signupsByResponse)
        {
            var signupsByUser = new Dictionary<ulong, IEnumerable<EventResponse>>();

            var userIds = signupsByResponse.Values
                .SelectMany(responseSignups => responseSignups)
                .Distinct();

            foreach (var userId in userIds)
            {
                var userResponses = signupsByResponse
                    .Where(responseSignups => responseSignups.Value.Contains(userId))
                    .Select(responseSignups => responseSignups.Key);

                signupsByUser.Add(userId, userResponses);
            }

            return signupsByUser;
        }

        private DiscordEmbedBuilder GetSignupEmbed(
            DiscordEvent discordEvent,
            Dictionary<EventResponse, IEnumerable<ulong>> signupsByResponse)
        {
            var userResponseDictionary = GetResponsesByUser(signupsByResponse);

            var signupEmbed = new DiscordEmbedBuilder
            {
                Title = $"{discordEvent.Name} - {discordEvent.Time:ddd dd MMM yyyy @ h:mm tt}",
                Description = discordEvent.Description,
                Footer = new DiscordEmbedBuilder.EmbedFooter
                {
                    Text = $"event key: {discordEvent.Key}"
                }
            };

            var usersField = string.Join(
                "\n",
                userResponseDictionary.Select(response => $"<@{response.Key}>")
            );

            signupEmbed.AddField("Participants", usersField, true);

            var responsesList = userResponseDictionary.Select(
                response => string.Join(" ", response.Value)
            );
            var responsesField = string.Join("\n", responsesList);

            signupEmbed.AddField("Response(s)", responsesField, true);

            var optionsField = string.Join(
                "\n",
                signupsByResponse.Select(response => $"{response.Key.Emoji} - {response.Key.ResponseName}")
            );

            signupEmbed.AddField("Response options", optionsField);

            return signupEmbed;
        }

        private async Task EditEventField(
            CommandContext context,
            int eventKey,
            string editField,
            DiscordEmbedBuilder eventEmbed)
        {
            switch (editField)
            {
                case "name":
                    await context.RespondAsync($"{context.Member.Mention} - enter the new event name.");
                    var newName = await GetUserResponse(context);
                    if (newName == null)
                    {
                        return;
                    }

                    eventEmbed.Title = newName;
                    await TryEditEvent(context, eventKey, eventEmbed, newName: newName);
                    break;
                case "description":
                    await context.RespondAsync($"{context.Member.Mention} - enter the new description.");
                    var newDescription = await GetUserResponse(context);
                    if (newDescription == null)
                    {
                        return;
                    }

                    eventEmbed.Description = newDescription;
                    await TryEditEvent(context, eventKey, eventEmbed, newDescription: newDescription);
                    break;
                case "time":
                    await context.RespondAsync($"{context.Member.Mention} - enter the new event time.");
                    var newTime = await GetUserTimeResponse(context);
                    if (newTime == null)
                    {
                        return;
                    }

                    eventEmbed.Timestamp = newTime.Value.DateTime;
                    await TryEditEvent(context, eventKey, eventEmbed, newTime: newTime.Value.DateTime);
                    break;
            }
        }

        private async Task TryEditEvent(
            CommandContext context,
            int eventKey,
            DiscordEmbedBuilder eventEmbed,
            string? newName = null,
            string? newDescription = null,
            DateTime? newTime = null)
        {
            try
            {
                await eventsSheetsService.EditEventAsync(eventKey, newDescription, newName, newTime);
                await context.RespondAsync($"{context.Member.Mention} - changes saved!", embed: eventEmbed);
            }
            catch (EventNotFoundException)
            {
                await context.RespondAsync($"{context.Member.Mention} - operation stopped: event not found");
            }
        }

        private async Task<DiscordEmbedBuilder?> GetEventEmbed(CommandContext context, int eventKey)
        {
            try
            {
                var discordEvent = await eventsSheetsService.GetEventAsync(eventKey);

                return new DiscordEmbedBuilder
                {
                    Title = discordEvent.Name,
                    Description = discordEvent.Description,
                    Timestamp = new DateTimeOffset(discordEvent.Time)
                };
            }
            catch (EventNotFoundException)
            {
                await context.RespondAsync($"{context.Member.Mention} - operation stopped: event doesn't exist.");
                return null;
            }
        }

        private async Task<string?> GetUserResponse(
            CommandContext context,
            string[]? validStrings = null)
        {
            var interactivity = context.Client.GetInteractivity();
            var response = interactivity.WaitForMessageAsync(
                message =>
                    IsValidResponse(message, context, validStrings),
                TimeSpan.FromSeconds(30)
            ).Result.Result?.Content;

            switch (response)
            {
                case "stop":
                    await context.RespondAsync($"{context.User.Mention} - operation stopped.");
                    return null;
                case null:
                    await context.RespondAsync($"{context.User.Mention} - timed out.");
                    break;
            }

            return response;
        }

        private async Task<bool?> GetUserConfirmation(CommandContext context)
        {
            var interactivity = context.Client.GetInteractivity();
            var response = interactivity.WaitForMessageAsync(
                message =>
                    IsValidResponse(message, context, ConfirmationResponses),
                TimeSpan.FromSeconds(30)
            ).Result.Result.Content;

            switch (response)
            {
                case "stop":
                    await context.RespondAsync($"{context.User.Mention} - operation stopped.");
                    return null;
                case null:
                    await context.RespondAsync($"{context.User.Mention} - timed out.");
                    break;
            }

            return response == "yes";
        }

        private async Task<DateTimeOffset?> GetUserTimeResponse(CommandContext context)
        {
            var response = await GetUserResponse(context);

            if (response == null)
            {
                return null;
            }

            try
            {
                return DateTimeOffset.Parse(response);
            }
            catch (FormatException exception)
            {
                await context.RespondAsync($"{context.Member.Mention} - operation stopped: {exception.Message}");
                return null;
            }
        }

        private async Task<int?> GetUserIntResponse(CommandContext context)
        {
            var response = await GetUserResponse(context);

            if (response == null)
            {
                return null;
            }

            try
            {
                return int.Parse(response);
            }
            catch (FormatException exception)
            {
                await context.RespondAsync($"{context.Member.Mention} - operation stopped: {exception.Message}");
                return null;
            }
        }

        private bool IsValidResponse(DiscordMessage response, CommandContext context, string[]? validStrings)
        {
            return context.User.Id == response.Author.Id &&
                   context.Channel == response.Channel &&
                   (validStrings == null || validStrings.Contains(response.Content));
        }
    }
}
