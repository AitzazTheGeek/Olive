using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Tasks;
using Olive.Entities;

namespace Olive.Audit
{
    public static class Audit
    {
        internal static List<ILogger> Providers = new List<ILogger>();

        public static Func<ClaimsPrincipal> GetUser =
            () =>
            {
                Debug.WriteLine("Audit.GetUser() is not configured");
                return null;
            };

        public static Func<string> GetUserIP =
            () =>
            {
                Debug.WriteLine("Audit.GetUserIP() is not configured");
                return null;
            };

        public static void Use(ILogger logger)
            => Providers.Add(logger ?? throw new ArgumentNullException(nameof(logger)));

        public static void Use(Func<ClaimsPrincipal> userProvider, Func<string> ipProvider)
        {
            GetUser = userProvider ?? GetUser;
            GetUserIP = ipProvider ?? GetUserIP;
        }

        public static Task LogInsert(IEntity entity)
        {
            if (entity is IAuditEvent) return Task.CompletedTask;
            if (!Config.Get<bool>("Database:Audit:Insert:Action")) return Task.CompletedTask;
            if (!LogEventsAttribute.ShouldLog(entity.GetType())) return Task.CompletedTask;

            var data = string.Empty;
            if (Config.Get("Database:Audit:Insert:Data", defaultValue: true))
                data = EntityProcessor.GetDataXml(entity);

            return Log("Insert", data, entity);
        }

        public static async Task LogUpdate(IEntity entity)
        {
            if (entity is IAuditEvent) return;
            if (!Config.Get<bool>("Database:Audit:Update:Action")) return;
            if (!LogEventsAttribute.ShouldLog(entity.GetType())) return;

            var data = string.Empty;
            if (Config.Get("Database:Audit:Update:Data", defaultValue: true))
            {
                data = await EntityProcessor.GetChangesXml(entity);
                if (data.IsEmpty()) return; // No changes have happened, ignore recording the action:
            }

            await Log("Update", data, entity);
        }

        public static Task LogDelete(IEntity entity)
        {
            if (entity is IAuditEvent) return Task.CompletedTask;
            if (!Config.Get<bool>("Database:Audit:Delete:Action")) return Task.CompletedTask;
            if (!LogEventsAttribute.ShouldLog(entity.GetType())) return Task.CompletedTask;

            var data = string.Empty;
            if (Config.Get("Database:Audit:Delete:Data", defaultValue: true))
            {
                var changes = Entity.Database.GetProvider(entity.GetType()).GetUpdatedValues(entity, null);
                data = EntityProcessor.ToChangeXml(changes);
            }

            return Log("Delete", data, entity);
        }

        public static Task Log(IAuditEvent auditEvent) => Providers.AwaitAll(x => x.Log(auditEvent));

        public static Task Log(string @event, string data)
            => Log(@event, data, null);

        public static Task Log(string @event, string data, IEntity item)
        {
            string userId = null;
            try { userId = GetUser()?.GetId(); }
            catch (Exception err) { Debug.WriteLine("Cannot get current user id:" + err); }

            string userIp = null;
            try { userIp = GetUserIP(); }
            catch (Exception err) { Debug.WriteLine("Cannot get current user ip:" + err); }

            return Log(@event, data, item, userId, userIp);
        }

        /// <summary>
        /// Logs the specified event as a record in the ApplicationEvents database table.
        /// </summary>
        /// <param name="event">The event title.</param>
        /// <param name="data">The details of the event.</param>
        /// <param name="item">The record for which this event is being logged (optional).</param>
        /// <param name="userId">The ID of the user involved in this event (optional). If not specified, the current ASP.NET context user will be used.</param>
        /// <param name="userIp">The IP address of the user involved in this event (optional). If not specified, the IP address of the current Http context (if available) will be used.</param>
        public static Task Log(string @event, string data, IEntity item, string userId, string userIp)
        {
            if (@event.IsEmpty()) throw new ArgumentNullException(nameof(@event));

            var tasks = new List<Task>();

            foreach (var provider in Providers)
            {
                var log = provider.CreateInstance();
                if (log == null) continue;

                log.ItemData = data;
                log.Event = @event;
                log.UserId = userId;
                log.UserIp = userIp;

                if (item != null)
                {
                    log.ItemType = item.GetType().FullName;
                    log.ItemId = item.GetId().ToString();
                }

                tasks.Add(provider.Log(log));
            }

            return Task.WhenAll(tasks);
        }
    }
}