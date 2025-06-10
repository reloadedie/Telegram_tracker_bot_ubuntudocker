using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;

namespace Telegram_KrotoBot
{
    public class Program : HelpClass
    {
        private static TelegramBotClient Bot;

        private static readonly string _configPath = Path.Combine("Config");
        private static string token { get; set; }

        static void Main(string[] args)
        {
            LoadTokenFromJsonConfig();
            Handlers.LoadConfigForAmdinsAndChannelsJson(_configPath, true);

            Thread newBotMethodThread = new Thread(BotMethodClassic);
            newBotMethodThread.Start();

           // Thread newBotMethodThreadWriteMessage = new Thread(BotMethodWritesMessagesInTelegramm);
           // newBotMethodThreadWriteMessage.Start();

            Thread newCaseMethodThread = new Thread(CaseMethod);
            newCaseMethodThread.Start();
        }

        /// <summary>
        /// загрузка токена из .json конфиг файла
        /// </summary>
        private static void LoadTokenFromJsonConfig()
        {
            try
            {
                // Указываем папку Config правильно
                var config = new ConfigurationBuilder()
                                 .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "Config"))
                                 .AddJsonFile("appsettings.json")
                                 .Build();

                token = config["BotToken"];

                if (string.IsNullOrEmpty(token))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Ошибка: токен бота не найден в конфиг файле");
                    Console.ResetColor();
                    return;
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// Метод для работы в консоли (на будущее)
        /// </summary>
        private static void CaseMethod()
        {
            while (true)
                switch (Console.ReadLine())
                {
                    case "/command":
                        Console.Beep();
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"какая-то команда выполнена.");
                        Console.ResetColor();
                        continue;

                    //cases 
                    #region
                    case "/help":
                        Console.Beep();
                        HelpClass.CaseHelpMethod();
                        continue;

                    case "/addword":
                        Console.WriteLine($"добавляем слово...");
                        HelpClass.CaseAddWordMethod();
                        continue;

                    case "/adduser":
                        Console.WriteLine($"добавляем пользователя...");
                        HelpClass.CaseAddUserMethod();
                        continue;

                    case "/deleteword":
                        Console.WriteLine($"удаляем слово...");
                        HelpClass.CaseDeleteWordMethod();
                        continue;

                    case "/deleteuser":
                        Console.WriteLine($"удаляем пользователя...");
                        HelpClass.CaseDeleteUserMethod();
                        continue;

                    case "/continue":
                        Console.WriteLine($"продолжаем очистку");
                        HelpClass.CaseContinueMethod();
                        continue;

                    case "/stop":
                        Console.WriteLine($"продолжаем очистку");
                        HelpClass.CaseStopMethod();
                        continue;

                    case "/clearconsole":
                        HelpClass.CaseClearConsoleMethod();
                        continue;

                    case "/exit":
                        HelpClass.CaseExitMethod();
                        continue;
                        #endregion
                }
        }

        /// <summary>
        /// Метод для запуска бота и отлова токена
        /// версия с обычным логированием в консоль и без ответов в тг (классическая)
        /// </summary>
        private static void BotMethodClassic()
        {
            try
            {
                Bot = new TelegramBotClient(token);
                var cancellationToken = new CancellationTokenSource();

                ReceiverOptions receiverOptions = new() { AllowedUpdates = { } };
                
                // перед тем, как запустить бота, нужно назначить чаты и их id для автоматического удаления сообщений бота
                var channelIds = Handlers.LoadConfigForAmdinsAndChannelsJson(_configPath, false);

                long[] chatIdsToClean = channelIds
                    .Where(id => long.TryParse(id, out _))
                    .Select(id => long.Parse(id))
                    .ToArray();

                // версия с простым логированием бота И БЕЗ ОТВЕТОВ НА СООБЩЕНИЯ В ТГ
                Console.WriteLine("Запущена версия с обычным логированием в консоль (без ответов в телеграмме)");
                Bot.StartReceiving(HandlersNoWriteMessages.HandleUpdateAsync,
                                   HandlersNoWriteMessages.HandleErrorAsync,
                                   receiverOptions,
                                   cancellationToken.Token);

                var me = Bot.GetMeAsync().Result;
                Handlers.SetBotId(me.Id);

                Console.Title = "Telegram Tracker Bot v.0.5";
                Console.WriteLine($"в кратце - что умеет бот?");
                Console.WriteLine("удалять спам сообщения, если человек не подписан или пишет плохие слова/ведёт спам атаку");
                Console.WriteLine($"Бот запущен и ждет сообщения...");
                Console.WriteLine($"Для бота есть команды. подробнее /help");
                Console.WriteLine("--------------------------------------------------");
                Console.ReadLine();
                cancellationToken.Cancel();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в BotMethodWritesMessagesInTelegramm: {ex.Message}");
                Console.WriteLine("Попытка перезапуска через 10 секунд...");
                Console.WriteLine(ex.Message);

                Thread.Sleep(10000);
                BotMethodClassic();
            }
        }

        /// <summary>
        /// Метод для запуска бота и отлова токена
        /// Расширенная версия работы бота с логированием в консоль и с ответами в телеграмме
        /// </summary>
        private static void BotMethodWritesMessagesInTelegramm()
        {
            try
            {
                Bot = new TelegramBotClient(token);
                var cancellationToken = new CancellationTokenSource();

                ReceiverOptions receiverOptions = new() { AllowedUpdates = { } };

                // перед тем, как запустить бота, нужно назначить чаты и их id для автоматического удаления сообщений бота
                var channelIds = Handlers.LoadConfigForAmdinsAndChannelsJson(_configPath, false);

                long[] chatIdsToClean = channelIds
                    .Where(id => long.TryParse(id, out _))
                    .Select(id => long.Parse(id))
                    .ToArray();

                // версия для работы бота с ответами на сообщения в тг
                Console.WriteLine("Запущена версия с расширенным логированием в консоль (с ответами в телеграмме)");
                Bot.StartReceiving(Handlers.HandleUpdateAsync,
                                   Handlers.HandleErrorAsync,
                                   receiverOptions,
                                   cancellationToken.Token);

                var me = Bot.GetMeAsync().Result;
                Handlers.SetBotId(me.Id);

                Console.Title = "Telegram Tracker Bot v.0.5";
                Console.WriteLine($"в кратце - что умеет бот?");
                Console.WriteLine("удалять спам сообщения, если человек не подписан или пишет плохие слова/ведёт спам атаку");
                Console.WriteLine($"Бот запущен и ждет сообщения...");
                Console.WriteLine($"Для бота есть команды. подробнее /help");
                Console.WriteLine("--------------------------------------------------");
                Console.ReadLine();
                cancellationToken.Cancel();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в BotMethodWritesMessagesInTelegramm: {ex.Message}");
                Console.WriteLine("Попытка перезапуска через 10 секунд...");
                Console.WriteLine(ex.Message);

                Thread.Sleep(10000);
                BotMethodWritesMessagesInTelegramm();
            }
        }

        /// <summary>
        /// Метод для запуска бота и отлова токена
        /// версия с обычным логированием в консоль и без ответов в тг (классическая)
        /// Метод в разработке (вернусь позже 9.06.25)
        /// </summary>
        private static void BotMethodClassicHttpCLient()
        {
            try
            {
                // есть идея настроить http клиент для улучшения пропускной способности
                var httpClient = new HttpClient(new HttpClientHandler
                {
                    MaxConnectionsPerServer = 30,
                    UseProxy = false,
                    Proxy = null
                })
                {
                    Timeout = TimeSpan.FromSeconds(60)
                };

                // Инициализация бота с настроенным HttpClient
                Bot = new TelegramBotClient(token, httpClient);
                var cancellationToken = new CancellationTokenSource();

                ReceiverOptions receiverOptions = new()
                {
                    AllowedUpdates = Array.Empty<UpdateType>(), // Оптимизация: указываем конкретные типы обновлений
                    ThrowPendingUpdates = true // Игнорируем накопившиеся обновления при старте
                };

                // Загрузка ID чатов для автоматического удаления сообщений
                var channelIds = Handlers.LoadConfigForAmdinsAndChannelsJson(_configPath, false);
                long[] chatIdsToClean = channelIds
                    .Where(id => long.TryParse(id, out _))
                    .Select(id => long.Parse(id))
                    .ToArray();

                // Настройка обработчиков с оптимизированным удалением сообщений
                Console.WriteLine("Запущена оптимизированная версия с ускоренным удалением сообщений");
                Bot.StartReceiving(
                    updateHandler: async (client, update, ct) =>
                    {
                        try
                        {
                            await HandlersNoWriteMessages.HandleUpdateAsync(client, update, ct);

                            // Дополнительная логика для быстрого удаления
                            if (update.Message != null && ShouldDeleteQuickly(update.Message))
                            {
                                var messagesToDelete = await FindRelatedMessagesToDelete(update.Message);
                                await FastDeleteMessages(client, update.Message.Chat.Id, messagesToDelete);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка обработки обновления: {ex.Message}");
                        }
                    },
                    errorHandler: async (client, exception, ct) =>
                    {
                        await HandlersNoWriteMessages.HandleErrorAsync(client, exception, ct);

                        // Специальная обработка FloodWait
                        if (exception is ApiRequestException apiEx && apiEx.Message.Contains("FLOOD_WAIT"))
                        {
                            var waitTime = int.Parse(apiEx.Message.Split('_').Last());
                            Console.WriteLine($"Обнаружен FloodWait. Ожидание {waitTime} секунд...");
                            await Task.Delay(waitTime * 1000, ct);
                        }
                    },
                    receiverOptions: receiverOptions,
                    cancellationToken: cancellationToken.Token
                );

                // Инициализация бота
                var me = Bot.GetMeAsync().Result;
                Handlers.SetBotId(me.Id);

                Console.Title = $"Telegram Tracker Bot v.0.6 | {me.Username}";
                Console.WriteLine($"Бот @{me.Username} запущен и работает в режиме ускоренного удаления");
                Console.WriteLine("Основные функции:");
                Console.WriteLine("- Автоматическое удаление спам-сообщений");
                Console.WriteLine("- Фильтрация запрещенных слов");
                Console.WriteLine("- Проверка подписки пользователей");
                Console.WriteLine($"\nДоступные команды: /help");
                Console.WriteLine(new string('-', 50));

                // Ожидание команды выхода
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Критическая ошибка в BotMethodClassic: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine("Попытка перезапуска через 10 секунд...");

                Thread.Sleep(10000);
                BotMethodClassicHttpCLient(); // Рекурсивный перезапуск
            }
        }

        // Дополнительные методы для оптимизированного удаления
        private static async Task FastDeleteMessages(ITelegramBotClient botClient, long chatId, IEnumerable<int> messageIds)
        {
            const int maxParallelDeletes = 5; // Оптимальное количество параллельных удалений
            var semaphore = new SemaphoreSlim(maxParallelDeletes, maxParallelDeletes);
            var random = new Random();

            var deleteTasks = messageIds.Select(async messageId =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await botClient.DeleteMessageAsync(chatId, messageId);
                    await Task.Delay(random.Next(50, 150)); // Рандомная задержка
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка удаления сообщения {messageId}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(deleteTasks);
        }

        private static bool ShouldDeleteQuickly(Message message)
        {
            // Ваша логика определения нужно ли удалять сообщение
            // Оптимизируйте этот метод для максимальной скорости
            return true;
        }

        private static async Task<List<int>> FindRelatedMessagesToDelete(Message triggerMessage)
        {
            var messagesToDelete = new List<int> { triggerMessage.MessageId };

            // Дополнительная логика поиска связанных сообщений для удаления
            // Например: предыдущие сообщения того же пользователя

            return messagesToDelete;
        }

    }
}
