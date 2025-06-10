using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Telegram_KrotoBot
{
    public class Handlers
    {
        // загрузка файлов из папки (в том числе и конфиг)
        #region

        // Листы с банвордами, черным и белым списками     
        #region
        private static readonly string DataPath = Path.Combine("Data");
        private static readonly string BlackListPath = Path.Combine(DataPath, "banuser_list.txt");
        private static readonly string SpamWordsPath = Path.Combine(DataPath, "banword_list.txt");
        private static readonly string WhiteListPath = Path.Combine(DataPath, "whiteuser_list.txt");
   
        private static List<string> listSpam = LoadListFromFile(SpamWordsPath);
        private static List<string> listBlackUsers = LoadListFromFile(BlackListPath);
        private static List<string> listWhiteUsers = LoadListFromFile(WhiteListPath);
        #endregion

        /// <summary>
        /// Загрузка списков .txt (тип List<string>)
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private static List<string> LoadListFromFile(string filePath)
        {
            var list = new List<string>();

            if (!System.IO.File.Exists(filePath))
            {
                Console.WriteLine($"⚠ Файл не найден: {filePath}");
                return list;
            }

            foreach (var line in System.IO.File.ReadAllLines(filePath))
            {
                string trimmedLine = line.Trim();

                if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                {
                    list.Add(trimmedLine);
                }
            }

            return list;
        }

        /// <summary>
        /// загрузка админов и каналов из конфига
        /// </summary>
        public static string[] LoadConfigForAmdinsAndChannelsJson(string _configPath, bool _isWriteMessage)
        {
            try
            {
                bool message_write = _isWriteMessage;

                // загрузка конфига
                #region

                var config = new ConfigurationBuilder()
                                 .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "Config"))
                                 .AddJsonFile("appsettings.json")
                                 .Build();
                #endregion

                // присвоение
                idChannels_mass = config.GetSection("ChannelsId").
                    Get<string[]>() ?? Array.Empty<string>();

                if (message_write)
                {
                    WriteConfigMessage(_configPath);
                }

                return idChannels_mass;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка с загрузкой каналов из " +
                    "конфиг файла .json,", ex.Message);
                return Array.Empty<string>();
            }
        }
        #endregion

        /// <summary>
        /// прослушивание новых сообщений
        /// работа с типами сообщений
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="update"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            var handler = update.Type
            switch
            {
                UpdateType.Message => BotOnMessageReceived(botClient, update.Message!),
               // UpdateType.Unknown => BotOnUnkownMessageReceived(botClient, update.Type),
                UpdateType.EditedMessage => BotOnMessageReceived(botClient, update.EditedMessage!), // измененное сообщение тоже может быть плохим
                _ => UnknownUpdateHandlerAsync(botClient, update)
            };

            try
            {
                await handler;
            }
            catch (Exception exception)
            {
                await HandleErrorAsync(botClient, exception, cancellationToken);
            }
        }

        /// <summary>
        /// что делает бот, когда получает сообщение?
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        private static async Task BotOnMessageReceived(ITelegramBotClient botClient, Message message)
        {
            // проверка сообщения
            #region

            // если это не текст - удаление :(
            if (message == null || message.Type == null || message.Type != MessageType.Text)
            {
                try
                {
                    await Task.Run(async () => BotTellsMessageAboutMessageType(botClient, message));
                    await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при удалении сообщения: {ex.Message}");
                }
                return;
            }

            // посыл правнукам
            /*
             * 
             * Проблема, которую вы описываете, связана с тем, что при 
             * проверке message.Type в блоке if вы не учитываете, что сам
             * объект message или его свойство Type может быть null. 
             * Когда вы помещаете этот код в try-catch, исключение возникает
             * потому, что вы пытаетесь обратиться к свойству Type объекта 
             * message, который может быть null
             if (message.Type != MessageType.Text)
            {
                await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);
                Console.WriteLine("Удалено сообщение (не является текстом)");
                return;
            }
            */

            try
            {
                Console.WriteLine("");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"Получено новое сообщение, его тип: {message.Type};" +
                    $" его длина {new StringInfo(message.Text).LengthInTextElements}");
                Console.ResetColor();

                // сначала проверим пользователя на подписку ✅
                await Task.Run(async () => CheckSubscribeMethod(botClient, message));

                // далее проверим на черный и белый списки 🏳️‍🌈🏳️‍🌈🏳️‍🌈
                await Task.Run(async () => CheckBlackWhiteListMethod(botClient, message, listWhiteUsers, listBlackUsers));

                // затем проверим на спам &#@^%!
                await Task.Run(async () => CheckSpamMethod(botClient, message, listSpam));

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("Ошибка с проверкой сообщения (метод проверки на тип и запуска тасок --> BotOnMessageReceived)");
            }

            #endregion
        }

        // целая секция на проверку подписки на каналы
        #region

       // private static string _channel_firstlink = "https://t.me/";
        private static string[] idChannels_mass { get; set; } = Array.Empty<string>();

        /// <summary>
        /// метод для проверки на подписку на канал. 
        /// удаление сообщения, если нет подписки
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        private static async Task CheckSubscribeMethod(ITelegramBotClient botClient, Message message)
        {
            try
            {
                bool isSubscribed = await IsUserSubscribedToAllChannels(botClient, message, message.From.Id, idChannels_mass);
                if (!isSubscribed)
                {
                    await BotWriteMessageAboutSubscribe(botClient, message);
                    await botClient.DeleteMessageAsync(
                        chatId: message.Chat.Id,
                        messageId: message.MessageId);

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в CheckSubscribeMethod: {ex.Message}");
            }
        }
        
        private static async Task<bool> IsUserSubscribedToAllChannels(ITelegramBotClient botClient, Message message, long userId, string[] channelIds)
        {
            foreach (var channelId in channelIds)
            {
                if (!await IsUserSubscribedToChannel(botClient, message, userId, channelId))
                {
                    return false;
                }
            }
            return true;
        }

        private static async Task<bool> IsUserSubscribedToChannel(ITelegramBotClient botClient, Message message, long userId, string channelId)
        {
            try
            {
                Chat chat;
                string parsedChannelId = channelId;

                Console.WriteLine($"Проверка подписки для пользователя " +
                    $"{userId} / {message.From} в канале {channelId}");

                // Обработка разных форматов channelId
                if (channelId.StartsWith("@"))
                {
                    chat = await botClient.GetChatAsync(channelId);
                    parsedChannelId = channelId;
                }
                else if (long.TryParse(channelId, out long id))
                {
                    // Если channelId числовой, добавляем "-100" (для супергрупп)
                    parsedChannelId = $"-100{id}";
                    chat = await botClient.GetChatAsync(parsedChannelId);
                }
                else
                {
                    Console.WriteLine($"Некорректный формат channelId: {channelId}");
                    return false;
                }

                // Логируем информацию о канале
                Console.WriteLine($"Канал: {chat.Title} (ID: {chat.Id}, тип: {chat.Type})");

                // Получаем информацию о пользователе в канале
                var member = await botClient.GetChatMemberAsync(chat.Id, userId);
                Console.WriteLine($"Статус пользователя: {member.Status}");

                // Проверяем, что пользователь подписан
                bool isSubscribed = member.Status is ChatMemberStatus.Member
                    or ChatMemberStatus.Administrator
                    or ChatMemberStatus.Creator;

                return isSubscribed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке подписки: {ex.Message}");
                return false;
            }
        }
        
        protected static string GetChannelLink(string channelId)
        {
            // Если это числовой ID (начинается с -100)
            if (channelId.StartsWith("-100") && long.TryParse(channelId, out _))
            {
                return $"[наш канал](https://t.me/c/{channelId.Substring(4)})";
            }

            // Если это @channel
            if (channelId.StartsWith("@"))
            {
                return $"[{channelId}](https://t.me/{channelId.Substring(1)})";
            }

            return channelId;
        }

        #endregion

        // проверки на списки и спам
        #region

        /// <summary>
        /// метод для проверки на пользователя в белом и в чёрном списках
        /// если есть сразу в двух - удаление всех сообщений
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        private static async Task CheckBlackWhiteListMethod(ITelegramBotClient botClient, Message message, List<string>whiteList, List<string> blackList)
        {
            try
            {
                bool boolWhiteUser = whiteList.Any(listU => message.From.ToString().Contains(listU));
                bool boolBlackUser = blackList.Any(listU => message.From.ToString().Contains(listU));

                // приоритет проверки 1. белый = черный список
                if (boolWhiteUser && boolBlackUser)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Произошла техническая шоколадка " +
                        $"сообщение: (тип {message.Type}), " +
                        $"пользователь " +
                        $"{message.From} " +
                        $"находится и в ЧЁРНОМ, и в БЕЛОМ списке. Исправьте это");
                    Console.ResetColor();
                    Console.Beep();
                    return;
                }

                // приоритет проверки 2. белый список
                if (boolWhiteUser)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Получено спам сообщение (тип {message.Type}), " +
                        $"но " +
                        $"{message.From} " +
                        $"находится в белом списке");
                    Console.ResetColor();
                    return;
                }

                // приоритет проверки 3. черный список (удаляем все)
                if (boolBlackUser)
                {
                    await BotWriteMessageAboutBlackList(botClient, message, blackList);
                    await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);

                   // await Task.Run(async () => await DeleteMessageFromBotMethod(botClient, message));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в CheckBlackWhiteListMethod: {ex.Message}");
            }
        }

        private static async Task CheckSpamMethod(ITelegramBotClient botClient, Message message, List<string> spamList)
        {
            // приоритет 4. удаление сообщения, если содержит спам слово
            try
            {
                bool yesSpam = spamList.Any(listS => message.Text.ToLower().Contains(listS));
                if (yesSpam)
                {
                    // сначала ответим, чтобы человеку было видно сообщение от бота (если поменять порядок - видно не будет)
                    await Task.Run(async () => BotWriteMessageAboutSpam(botClient, message, spamList));
                    // затем удалим нежелательное сообщение
                    await botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId);

                    // добавление второго await в самой таске иногда меняет в худшую сторону

                    // и только потом своё (через некоторое время)
                    await Task.Run(async() => DeleteMessageFromBotMethod(botClient, message));
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Ошибка в CheckSpamMethod: {ex.Message}");
            }
        }

        #endregion

        // секция про ответы бота и удаление сообщений от бота (телеграмм)
        #region

        private static async Task BotWriteMessageAboutSubscribe(ITelegramBotClient botClient, Message message)
        {
            Console.WriteLine($"Пользователь {message.From.Id} ({message.From}) не подписан, поэтому сообщение удалено");

            var warningMessage = $"Уважаемый {message.From.Username ?? message.From.FirstName}, для участия в обсуждении необходимо подписаться на наши каналы:\n" +
                                string.Join("\n", idChannels_mass.Select(id => $"- {GetChannelLink(id)}")) +
                                "\nПосле подписки вы сможете писать сообщения";

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: warningMessage,
                replyToMessageId: message.MessageId,
                disableNotification: true);
        }

        /// <summary>
        /// Бот пишет о нахождении пользователя в черном списке
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        /// <param name="blackList"></param>
        /// <returns></returns>
        private static async Task BotWriteMessageAboutBlackList(ITelegramBotClient botClient, Message message, List<string> blackList)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Удалено black_user сообщение (тип {message.Type}), {message.From}" +
                $" находится в чёрном списке");
            Console.ResetColor();

            await botClient.SendTextMessageAsync(message.Chat.Id,
                                                 message.Text =
                $"{message.From} Вы находитесь в чёрном списке. " +
                $"Мы удаляем любые сообщения от пользователей в черном списке",
                                                 disableNotification: true,
                                                 replyToMessageId: message.MessageId
                                                 );
        }

        /// <summary>
        /// Бот пишет сообщение об обнаруженном спаме
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        /// <param name="spamList"></param>
        /// <returns></returns>
        private static async Task BotWriteMessageAboutSpam(ITelegramBotClient botClient, Message message, List<string> spamList)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Удалено спам - сообщение (тип {message.Type}),"
               + $" написал его {message.From}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($" сообщение с бан-словом: {message.Text}");
            Console.ResetColor();

            var foundWords = spamList.Where(word => message.Text.ToLower().
                                      Contains(word.ToLower())).ToList();

            var warningMessage = $"{message.From.FirstName} / {message.From} ),\n" +
                                "Ваше сообщение содержит бан-слово, поэтому мы его удалили. " +
                                "Если вы напишете его еще раз \n" +
                                $"({string.Join(", ", foundWords)})\n" +
                                "то окажетесь в черном списке.";

            await botClient.SendTextMessageAsync(message.Chat.Id,
                                                 message.Text = warningMessage,
                                                 disableNotification: true,
                                                 replyToMessageId: message.MessageId);

        }

        /// <summary>
        /// таска для ответа бота и логирования в консоль на предмет типа сообщения
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        private static async Task BotTellsMessageAboutMessageType(ITelegramBotClient botClient, Message message)
        {
            Console.WriteLine($"Удалено сообщение (не является текстом), но это было {message.Type}");

            var warningMessage = $"Уважаемый {message.From.Username ?? message.From.FirstName}, в нашем" +
                $"канале можно писать только текстовые сообщения";

            await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: warningMessage,
                replyToMessageId: message.MessageId,
                disableNotification: true);
        }

        private static long _botId; // Кеш ID
        private static readonly HashSet<(long ChatId, int MessageId)> _botMessages = new();
        /// <summary>
        /// Метод для удаления одного сообщения бота (в разработке)
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        private static async Task DeleteMessageFromBotMethod(ITelegramBotClient botClient, Message message)
        {
            var messageToDeleteId = message.MessageId - 1;
            var chatId = message.Chat.Id;
            try
            {
                // При инициализации бота:
                // _botId = (await botClient.GetMeAsync()).Id;

                await Task.Delay(5000);

                if (_botMessages.Contains((chatId, messageToDeleteId)))
                {
                    await botClient.DeleteMessageAsync(
                        chatId: chatId,
                        messageId: messageToDeleteId
                    );

                    // Удаляем ID из кеша после успешного удаления
                    _botMessages.Remove((chatId, messageToDeleteId));
                }
                else
                {
                    Console.WriteLine("Попытка удалить сообщение бота выдало ошибку");
                }
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400)
            {
                Console.WriteLine("Сообщение уже удалено или не существует");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при удалении сообщения: {ex.Message}");
            }
        }

        /// <summary>
        /// нужен для присвоения чат id к DeleteAllMessagesFromBot
        /// </summary>
        /// <param name="botId"></param>
        public static void SetBotId(long botId)
        {
            _botId = botId;
        }

        public static async Task DeleteAllMessagesFromBot(ITelegramBotClient botClient, string chatIdOrUsername, CancellationToken cancellationToken = default)
        {
            try
            {
                Chat chat;
                long chatId;

                if (chatIdOrUsername.StartsWith("@"))
                {
                    chat = await botClient.GetChatAsync(chatIdOrUsername, cancellationToken);
                    chatId = chat.Id;
                }
                else if (long.TryParse(chatIdOrUsername, out long numericId))
                {
                    string formattedId = numericId.ToString().StartsWith("-100") ?
                        numericId.ToString() : $"-100{numericId}";
                    chat = await botClient.GetChatAsync(formattedId, cancellationToken);
                    chatId = chat.Id;
                }
                else
                {
                    Console.WriteLine($"Некорректный формат ID чата: {chatIdOrUsername}");
                    return;
                }

                Console.WriteLine($"Запущена очистка сообщений бота в чате: {chat.Title} (ID: {chatId})");

                int lastMessageId = 0;
                const int messagesLimit = 20; // Лимит сообщений за один запрос

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Получаем сообщения через GetUpdates
                        var updates = await botClient.GetUpdatesAsync(
                            offset: lastMessageId + 1,
                            limit: messagesLimit,
                            timeout: 0,
                            cancellationToken: cancellationToken);

                        if (updates.Length == 0)
                        {
                            // Если новых сообщений нет, ждем перед следующей проверкой
                            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                            continue;
                        }

                        // Фильтруем сообщения от нашего бота в нужном чате
                        var botMessages = updates
                            .Where(u => u.Message?.Chat.Id == chatId &&
                                       u.Message.From?.Id == _botId)
                            .OrderBy(u => u.Message.MessageId)
                            .ToList();

                        if (!botMessages.Any())
                        {
                            Console.WriteLine($"Новых сообщений бота не найдено в чате {chat.Title}");
                        }
                        else
                        {
                            Console.WriteLine($"Найдено {botMessages.Count} сообщений бота для удаления");

                            foreach (var update in botMessages)
                            {
                                try
                                {
                                    await botClient.DeleteMessageAsync(
                                        chatId: chatId,
                                        messageId: update.Message.MessageId,
                                        cancellationToken: cancellationToken);

                                    Console.WriteLine($"Удалено сообщение [{update.Message.MessageId}] " +
                                        $"от {update.Message.Date:g}: {Truncate(update.Message.Text, 30)}");

                                    // Обновляем lastMessageId
                                    lastMessageId = Math.Max(lastMessageId, update.Message.MessageId);

                                    // Пауза между удалениями
                                    await Task.Delay(500, cancellationToken);
                                }
                                catch (ApiRequestException ex) when (ex.ErrorCode == 400)
                                {
                                    Console.WriteLine($"Сообщение {update.Message.MessageId} уже удалено");
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Ошибка при удалении сообщения {update.Message.MessageId}: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при получении обновлений: {ex.Message}");
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    }

                    // Ожидание перед следующей проверкой
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка в DeleteAllMessagesFromBot: {ex.Message}");
            }
        }

        /// <summary>
        /// Вспомогательный метод для сокращения длинного текста
        /// </summary>
        /// <param name="text"></param>
        /// <param name="maxLength"></param>
        /// <returns></returns>
        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "[no text]";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private static void WriteConfigMessage(string config)
        {
            try
            {
                if (!System.IO.File.Exists(config))
                {
                    Console.WriteLine($"Файл конфигурации не найден: {config}");
                }
                else Console.WriteLine("Файл конфигурации найден");

                Console.WriteLine($"Каналы на проверку подписки: " +
                                    $"{idChannels_mass.Length}");
                Console.WriteLine("Их названия:");
                for (int i = 0; i < idChannels_mass.Length; i++)
                {
                    string channel = idChannels_mass[i];
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{channel}");
                }
                Console.ResetColor();
                Console.WriteLine("Сверьте данные");
                Console.WriteLine("");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        #endregion

        // секция про логирование CW (консоль)
        #region
        private static async Task BotWriteMessageAboutSubscribeConsole(ITelegramBotClient botClient, Message message)
        {
            Console.WriteLine($"Пользователь {message.From.Id} " +
                $"({message.From}) не подписан, " +
                $"поэтому сообщение удалено");
        }

        /// <summary>
        /// Бот пишет о нахождении пользователя в черном списке
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        /// <param name="blackList"></param>
        /// <returns></returns>
        private static async Task BotWriteMessageAboutBlackListConsole(ITelegramBotClient botClient, Message message, List<string> blackList)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Удалено black_user сообщение (тип {message.Type}), {message.From}" +
                $" находится в чёрном списке");
            Console.ResetColor();
        }

        /// <summary>
        /// Бот пишет сообщение об обнаруженном спаме
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        /// <param name="spamList"></param>
        /// <returns></returns>
        private static async Task BotWriteMessageAboutSpamConsole(ITelegramBotClient botClient, Message message, List<string> spamList)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Удалено спам - сообщение (тип {message.Type}),"
               + $" написал его {message.From}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($" сообщение с бан-словом: {message.Text}");
            Console.ResetColor();
        }

        /// <summary>
        /// таска для ответа бота и логирования в консоль на предмет типа сообщения
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        private static async Task BotTellsMessageAboutMessageTypeConsole(ITelegramBotClient botClient, Message message)
        {
            Console.WriteLine($"Удалено сообщение (не является текстом), но это было {message.Type}");
        }

        private static void WriteConfigMessageConsole(string config)
        {
            try
            {
                if (!System.IO.File.Exists(config))
                {
                    Console.WriteLine($"Файл конфигурации не найден: {config}");
                }
                else Console.WriteLine("Файл конфигурации найден");

                Console.WriteLine($"Каналы на проверку подписки: " +
                                    $"{idChannels_mass.Length}");
                Console.WriteLine("Их названия:");
                for (int i = 0; i < idChannels_mass.Length; i++)
                {
                    string channel = idChannels_mass[i];
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{channel}");
                }
                Console.ResetColor();
                Console.WriteLine("Сверьте данные");
                Console.WriteLine("");

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        #endregion

        //unkowns + errors nadlers
        #region

        /// <summary>
        /// Если вдруг прилетел неизвестный тип сообщения (heckers???)
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="update"></param>
        /// <returns></returns>
        private static Task UnknownUpdateHandlerAsync(ITelegramBotClient botClient, Update update)
        {
            Console.WriteLine($"Неизвестный тип сообщения: {update.Type}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Метод для обработки ошибок
        /// </summary>
        /// <param name="botClient"></param>
        /// <param name="exception"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception
            switch
            {
                ApiRequestException apiRequestException =>
                $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };
            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
        #endregion
    }
}
