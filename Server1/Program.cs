using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using Server1;
using Newtonsoft.Json;
using SocketServer.Ciphers;
using System.Threading.Tasks;
using System.Data.Entity;
using System.Windows;
using System.ComponentModel;
using Server1.DB;
using System.Security.Cryptography;

namespace SocketServer
{
    class Program
    {
        /// <summary>
        /// Сокет, который слушает новые подключения
        /// </summary>
        static Socket sListener;
        /// <summary>
        /// Список сокетов клиентов
        /// </summary>
        static List<Client> clients = new List<Client>();

        /// <summary>
        /// База данных
        /// </summary>
        //DatabaseContext db = new DatabaseContext();

        static string fileNameDB;

        /// <summary>
        /// Текстовая переменная, в которую записывается вся история чата
        /// </summary>
        static string history = "";

        /// <summary>
        /// Основная функция
        /// </summary>
        static void Main(string[] args)
        {
            Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\Логи"); //Создаём папку для логов
            Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\Выгрузки"); //Создаём папку для файлов
            File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", "[" + DateTime.Now.ToString("HH:mm:ss") + "] Лог сервера:\r\n");  //Создаём файл логов

            fileNameDB = Directory.GetCurrentDirectory() + "\\db.csv";

            if (!File.Exists(fileNameDB))
            {
                File.Create(fileNameDB);  //Создаём файл БД
            }

            // Устанавливаем для сокета локальную конечную точку
            IPHostEntry ipHost = getIpHost(); //Выбор Ip хоста
            IPAddress ipAddr = ipHost.AddressList.FirstOrDefault((a) => a.AddressFamily == AddressFamily.InterNetwork);
            IPEndPoint ipEndPoint = new IPEndPoint(ipAddr, 1001);

            Console.Write("[" + DateTime.Now.ToString("HH:mm:ss") + "] Лог сервера:\r\n");

            //Создаем сокет Tcp/Ip
            sListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            Program program = new Program();

            try
            {
                Task.Factory.StartNew(() =>
                {
                    program.ReceiveAndSend();
                }); //Создание и запуск нового потока

                sListener.Bind(ipEndPoint); //Связывает сокет с локальной конечной точкой

                // Начинаем слушать новые соединения
                sListener.Listen(10);

                while (true)
                {
                    //Программа приостанавливается, ожидая входящее соединение
                    Socket newSocket = sListener.Accept();
                    Client client = new Client(); //Создание нового клиента
                    client.socket = newSocket; //Присвоение ему его сокета
                    clients.Add(client); //Добавление клиента в список
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message + "\r\n" + ex.StackTrace + "\r\n");
                File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", ex.Message + "\r\n" + ex.StackTrace + "\r\n");
            }
            finally
            {
                Console.ReadLine();
            }
        }

        /// <summary>
        /// Проверка соответствия пароля
        /// </summary>
        /// <param name="name"></param>
        /// <param name="passwprd"></param>
        /// <returns></returns>
        private bool checkPassword(User user2)
        {
            User user = searchUser(user2.Name);

            if (user.Password == user2.Password)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Регистрация нового пользователя
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public bool registration(User user)
        {
            /*
            if (searchUser(user.Name) != null)
            {
                return false;
            }
            else
            {
                File.AppendAllText(fileNameDB, user.Name + "," + user.Password);
                return true;
            }
            */
            
            if (searchUser(user.Name) != null)
            {
                return false;
            }

            using (var db = new DatabaseContext())
            {
                db.Users.Add(user);
                db.SaveChanges();
            }

            /*
            db.Users.Add(user);
            db.SaveChanges();
            */

            return true;
            
        }

        /// <summary>
        /// Поиск пользователя в БД
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private User searchUser(string name)
        {
            /*
            string[] row = File.ReadAllLines(fileNameDB);

            for (int i = 0; i < row.Length; i++)
            {
                string[] cell = row[i].Split(',');

                if (cell[0] == name)
                {
                    User user = new User();
                    user.Name = cell[0];
                    user.Password = Convert.ToInt32(cell[1]);

                    return user;
                }
            }

            return null;
            */

            User user;

            using (var db = new DatabaseContext())
            {
                user = db.Users.FirstOrDefault(d => d.Name == name);
            }

            return user;
            
        }

        /// <summary>
        /// Возвращает юзера из пакета
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public User getUser(byte[] data)
        {
            string message = Encoding.GetEncoding(866).GetString(data); //Декодируем байты в текст

            Stribog stribog = new Stribog(Stribog.lengthHash.Length256);

            User user = JsonConvert.DeserializeObject<User>(message); //Десериализуем данные из формата Json в класс User
            user.key = stribog.GetHash(user.key);

            return user;
        }

        /// <summary>
        /// Приём пакетов и ответ на них
        /// </summary>
        public void ReceiveAndSend()
        {
            //Вечно перебираем всех клиентов и проверям, пришло ли что-то от них
            while (true)
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    //Если ни чего не пришло в течении 100 мс, то переходим к следующему клиенту в списке
                    if (!clients[i].socket.Poll(100, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    //Иначе...
                    try
                    {
                        string message;
                        string fileName;
                        User user;

                        //...получаем пакет из буфера входящего потока
                        Packet packet = GetPacket(clients[i]);

                        //В зависимости от команды, выполняется то или иное действие
                        switch (packet.commanda)
                        {
                            case "Сообщение":
                                message = Decrypt(packet, clients[i].user.key); //Дешифруем данные из пакета с помощью ключа
                                message = JsonConvert.DeserializeObject<string>(message); //Десериализуем данные из формата Json в string
                                message = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + clients[i].user.Name + " : " + message; //Прикрепляем время
                                Send("Сообщение", message, packet); //Отправляем присланное сообщение всем клиентам
                                break;
                            case "Регистрация":
                                user = getUser(packet.data);

                                if (registration(user))
                                {
                                    clients[i].user = user;

                                    message = "В чате зарегистрировался: " + clients[i].user.Name + " [" + clients[i].socket.RemoteEndPoint + "]\r\n"; //Формируем информацию о подключении
                                    Send("Сообщение", message, packet.cipher); //Отправляем её всем клиентам
                                    Thread.Sleep(1000);
                                    ObnovlenieInfo(clients[i], packet.cipher); //Обновляем информацию о клинтах и файлах
                                }
                                else
                                {
                                    Send("Ошибка", "Пользователь с таким именем уже существует.", clients[i]);
                                }

                                break;
                            case "Подключиться":
                                user = getUser(packet.data);

                                if (searchUser(user.Name) == null)
                                {
                                    Send("Ошибка", "Данного пользователя не существует.", clients[i]);
                                    break;
                                }

                                if (checkPassword(user))
                                {
                                    clients[i].user = user;

                                    message = "К чату подключился: " + clients[i].user.Name + " [" + clients[i].socket.RemoteEndPoint + "]\r\n"; //Формируем информацию о подключении
                                    Send("Сообщение", message, packet.cipher); //Отправляем её всем клиентам
                                    Thread.Sleep(1000);
                                    ObnovlenieInfo(clients[i], packet.cipher); //Обновляем информацию о клинтах и файлах
                                }
                                else
                                {
                                    Send("Ошибка", "Неверный пароль.", clients[i]);
                                }

                                break;
                            case "Отключиться":

                                if (clients[i].user == null)
                                {
                                    clients[i].socket.Disconnect(false);
                                    clients.RemoveAt(i);
                                }
                                else
                                {
                                    message = "От чата отключился: " + clients[i].user.Name + "\r\n";
                                    clients[i].socket.Disconnect(false);
                                    clients.RemoveAt(i);
                                    Send("Сообщение", message, packet.cipher);
                                    ObnovlenieInfo(clients[i], packet.cipher);
                                }
                                break;
                            case "Синхронизация":
                                //Синхронизация
                                break;
                            case "История":
                                Send("Сообщение", history, clients[i]);
                                break;
                            case "Выгрузить":
                                {
                                    message = Decrypt(packet, clients[i].user.key);
                                    fileName = JsonConvert.DeserializeObject<string>(message); //Получаем из файла, который клиент хочет выгрузить

                                    Send("Синхронизация", clients[i]);

                                    //Создаем файл в папке
                                    FileStream stream = new FileStream(Directory.GetCurrentDirectory() + "\\Выгрузки\\" + fileName, FileMode.Create, FileAccess.Write);
                                    BinaryWriter f = new BinaryWriter(stream);
                                    byte[] buffer = new byte[8192]; //Буфер для файла
                                    byte[] bFSize = new byte[512]; //Размер файла

                                    int bytesiRec = clients[i].socket.Receive(bFSize); //Принимаем размер
                                    Send("Синхронизация", clients[i]);
                                    int fSize = Convert.ToInt32(Encoding.GetEncoding(866).GetString(bFSize, 0, bytesiRec)); //Размер файла

                                    int processed = 0; //Байт принято
                                    while (processed < fSize) //Принимаем файл
                                    {
                                        //Если принятый кусок файла меньше 8192 (бит?), то вычисляем размер остатка и записываем на диск
                                        if ((fSize - processed) < 8192)
                                        {
                                            int bytesi = (fSize - processed);
                                            byte[] buf = new byte[bytesi];
                                            bytesi = clients[i].socket.Receive(buf);
                                            f.Write(buf, 0, bytesi); //Записываем принятый кусок файла (байты) на жёсткий диск
                                        }
                                        else
                                        {
                                            int bytesi = clients[i].socket.Receive(buffer);
                                            f.Write(buffer, 0, bytesi); //Записываем принятый кусок файла (байты) на жёсткий диск
                                        }
                                        Send("Синхронизация", clients[i]);
                                        processed += 8192;
                                    }
                                    f.Close();
                                    stream.Close(); //Закрываем запись в файл

                                    Thread.Sleep(1000);
                                    message = clients[i].user.Name + " выгрузил на сервер файл: " + fileName + "\r\n";
                                    Send("Сообщение", message, packet.cipher); //Отправляем информацию о выгрузки клиентам
                                    Thread.Sleep(1000);
                                    /*
                                    Send("!логины" + loginsMsg() + "\r\n");
                                    clients[i].Receive(ping);
                                    */
                                    Send("Файлы", fileMsgStringArray(), packet.cipher); //Обновляем информацию о файлах у клиентов
                                }
                                break;
                            case "Загрузить":
                                {
                                    //Аналогично Выгрузке, только загрузка
                                    message = Decrypt(packet, clients[i].user.key);
                                    fileName = Directory.GetCurrentDirectory() + "\\Выгрузки\\" + JsonConvert.DeserializeObject<string>(message);

                                    byte[] ping = new byte[1] { 0 }; //Синхронизация
                                    Send("Загрузить", Path.GetFileName(fileName), clients[i]);

                                    if (!Sinhronizatiya(clients[i]))
                                    {
                                        return;
                                    }

                                    FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                                    BinaryReader f = new BinaryReader(stream);
                                    byte[] buffer = new byte[8192]; //Буфер для файла
                                    int bytesi = 8192;
                                    int fSize = Convert.ToInt32(stream.Length);

                                    byte[] bFSize = Encoding.GetEncoding(866).GetBytes(Convert.ToString(fSize)); //Размер файла
                                    clients[i].socket.Send(bFSize); //Передаем размер
                                    if (!Sinhronizatiya(clients[i]))
                                    {
                                        return;
                                    }

                                    int processed = 0; //Байт передано
                                    while (processed < fSize) //Передаем файл
                                    {
                                        if ((fSize - processed) < 8192)
                                        {
                                            bytesi = Convert.ToInt32(fSize - processed);
                                            byte[] buf = new byte[bytesi];
                                            f.Read(buf, 0, bytesi);
                                            clients[i].socket.Send(buf);
                                        }
                                        else
                                        {
                                            f.Read(buffer, 0, bytesi);
                                            clients[i].socket.Send(buffer);
                                        }

                                        if (!Sinhronizatiya(clients[i]))
                                        {
                                            return;
                                        }
                                        processed += 8192;
                                    }
                                    f.Close();
                                    stream.Close();
                                }
                                break;
                            default:
                                break;
                        }
                    }
                     catch (Exception ex)
                    {
                        Console.Write(ex.Message + "\r\n" + ex.StackTrace + "\r\n");
                        File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", ex.Message + "\r\n" + ex.StackTrace + "\r\n");
                        //clients.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// Функция, которая формирует массив логинов клиентов
        /// </summary>
        /// <returns></returns>
        private static string[] loginsMsgStringArray()
        {
            string[] result = new string[clients.Count];

            for (int k = 0; k < clients.Count; k++)
            {
                result[k] = clients[k].user.Name;
            }
            return result;
        }

        /// <summary>
        /// Формирует массив класса FileInfoKratko, который содержит информацию о названии и размере файлов
        /// </summary>
        /// <returns></returns>
        private static FileInfoKratko[] fileMsgStringArray()
        {
            string[] files = Directory.GetFiles(Directory.GetCurrentDirectory() + "\\Выгрузки");

            FileInfoKratko[] result = new FileInfoKratko[files.Length];

            for (int k = 0; k < files.Length; k++)
            {
                FileInfo fileInfo = new FileInfo(files[k]);
                result[k] = new FileInfoKratko();
                result[k].name = fileInfo.Name;
                result[k].size = fileInfo.Length;
            }

            return result;
        }

        /// <summary>
        /// Обновляет информацию о клиентах и файлах у клиентов
        /// </summary>
        /// <param name="client"></param>
        /// <param name="cipher"></param>
        private static void ObnovlenieInfo(Client client, string cipher)
        {
            Send("Логины", loginsMsgStringArray(), cipher);

            if (Sinhronizatiya(client))
            {
                Send("Файлы", fileMsgStringArray(), client);
            }
        }

        /// <summary>
        /// Синхронизация
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        private static bool Sinhronizatiya(Client client)
        {
            if (GetPacket(client).commanda == "Синхронизация")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Формирует пакет из присланного массива байт
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        private static Packet GetPacket(Client client)
        {
            byte[] bytes = new byte[1024];
            int bytesRec = client.socket.Receive(bytes); //Забираем массив байт из буфера входящего потока
            string data = Encoding.GetEncoding(866).GetString(bytes, 0, bytesRec); //Декодируем в текст (Json)
            Packet packet = JsonConvert.DeserializeObject<Packet>(data); //Десериализуем из Json в Packet

            return packet;
        }


        /// <summary>
        /// Дешифратор
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        private static string Decrypt(Packet packet, byte[] key)
        {
            string result = "";
            int sizeBlock;

            //В зависимости от шифра, выполняются разные действия
            switch (packet.cipher)
            {
                case "Нет":
                case null:
                    //Если шифра нет, то просто декодируем байты в текст
                    result = Encoding.GetEncoding(866).GetString(packet.data);
                    break;
                case "Кузнечик":
                    Kuznyechik kuznyechik = new Kuznyechik();
                    sizeBlock = 16;

                    //Количество байт
                    byte[] dataD = new byte[packet.data.Length];

                    //В цикле формируем блоки указанного размера...
                    for (int i = 0; i < packet.data.Length; i += sizeBlock)
                    {
                        byte[] data8bytes = new byte[sizeBlock];

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            data8bytes[k] = packet.data[k + i];
                        }

                        //...и дешифруем каждый блок в отдельности с помощью ключа
                        byte[] dataD8bytes = kuznyechik.decrypt(data8bytes, key);

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            dataD[k + i] = dataD8bytes[k];
                        }
                    }

                    //Декодируем расшифрованный набор байт в текст
                    result = Encoding.GetEncoding(866).GetString(dataD);
                    result = result.Replace("┼", ""); //Убираем симвлоы заполнения блока
                    break;
                case "Магма":
                    //Аналогично Кузнечику
                    Magma magma = new Magma();
                    sizeBlock = 8;
                    dataD = new byte[packet.data.Length];

                    for (int i = 0; i < packet.data.Length; i += sizeBlock)
                    {
                        byte[] data8bytes = new byte[sizeBlock];

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            data8bytes[k] = packet.data[k + i];
                        }

                        byte[] dataD8bytes = magma.Decode(data8bytes, key);

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            dataD[k + i] = dataD8bytes[k];
                        }
                    }

                    result = Encoding.GetEncoding(866).GetString(dataD);
                    result = result.Replace("┼", "");
                    break;
                case "AES":
                    //Аналогично Кузнечику
                    sizeBlock = 16;

                    Aes aes = Aes.Create();
                    aes.Mode = CipherMode.ECB;
                    aes.KeySize = 256;
                    aes.BlockSize = sizeBlock * 8;
                    aes.Key = key;
                    aes.Padding = PaddingMode.Zeros;

                    ICryptoTransform encryptor = aes.CreateDecryptor();

                    dataD = new byte[packet.data.Length];

                    for (int i = 0; i < packet.data.Length; i += sizeBlock)
                    {
                        byte[] data8bytes = new byte[sizeBlock];

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            data8bytes[k] = packet.data[k + i];
                        }

                        byte[] dataD8bytes = encryptor.TransformFinalBlock(data8bytes, 0, sizeBlock);

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            dataD[k + i] = dataD8bytes[k];
                        }
                    }

                    result = Encoding.GetEncoding(866).GetString(dataD);
                    result = result.Replace("┼", "");
                    break;
                default:
                    result = null;
                    break;
            }

            return result;
        }

        /// <summary>
        /// Зашифратор
        /// </summary>
        /// <param name="data"></param>
        /// <param name="key"></param>
        /// <param name="cipher"></param>
        /// <returns></returns>
        private static byte[] Encrypt(string data, byte[] key, string cipher)
        {
            byte[] result;
            int sizeBlock;

            switch (cipher)
            {
                case "Нет":
                    result = Encoding.GetEncoding(866).GetBytes(data);
                    break;
                case "Кузнечик":
                    Kuznyechik kuznyechik = new Kuznyechik();
                    sizeBlock = 16;

                    //Если количество блоков не пропорционально размеру блока, то дописываем символы заполнения
                    while (data.Length % sizeBlock != 0)
                    {
                        data += "┼";
                    }

                    //Кодируем текст в массив байт
                    byte[] dataBytes = Encoding.GetEncoding(866).GetBytes(data);
                    result = new byte[dataBytes.Length];
                    
                    //В цикле формируем блоки...
                    for (int i = 0; i < dataBytes.Length; i += sizeBlock)
                    {
                        byte[] data8bytes = new byte[sizeBlock];

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            data8bytes[k] = dataBytes[k + i];
                        }

                        //...и зашфровывем эти блоки с помощью ключа
                        byte[] dataE8bytes = kuznyechik.encrypt(data8bytes, key);

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            result[k + i] = dataE8bytes[k];
                        }
                    }
                    break;
                case "Магма":
                    //Аналогично Кузнечику
                    Magma magma = new Magma();
                    sizeBlock = 8;

                    while (data.Length % sizeBlock != 0)
                    {
                        data += "┼";
                    }

                    dataBytes = Encoding.GetEncoding(866).GetBytes(data);
                    result = new byte[dataBytes.Length];

                    for (int i = 0; i < dataBytes.Length; i += sizeBlock)
                    {
                        byte[] data8bytes = new byte[sizeBlock];

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            data8bytes[k] = dataBytes[k + i];
                        }

                        byte[] dataE8bytes = magma.Encode(data8bytes, key);

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            result[k + i] = dataE8bytes[k];
                        }
                    }
                    break;
                case "AES":
                    //Аналогично Кузнечику
                    sizeBlock = 16;

                    Aes aes = Aes.Create();
                    aes.Mode = CipherMode.ECB;
                    aes.KeySize = 256;
                    aes.BlockSize = sizeBlock * 8;
                    aes.Key = key;
                    aes.Padding = PaddingMode.Zeros;

                    ICryptoTransform encryptor = aes.CreateEncryptor();

                    while (data.Length % sizeBlock != 0)
                    {
                        data += "┼";
                    }

                    dataBytes = Encoding.GetEncoding(866).GetBytes(data);
                    result = new byte[dataBytes.Length];

                    for (int i = 0; i < dataBytes.Length; i += sizeBlock)
                    {
                        byte[] data8bytes = new byte[sizeBlock];

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            data8bytes[k] = dataBytes[k + i];
                        }

                        byte[] dataE8bytes = encryptor.TransformFinalBlock(data8bytes, 0, sizeBlock);

                        for (int k = 0; k < sizeBlock; k++)
                        {
                            result[k + i] = dataE8bytes[k];
                        }
                    }
                    break;

                default:
                    result = null;
                    break;
            }

            return result;
        }

        /// <summary>
        /// Отправляет пакет всем клиентам
        /// </summary>
        /// <param name="commanda"></param>
        /// <param name="messageObject"></param>
        /// <param name="cipher"></param>
        private static void Send(string commanda, object messageObject, string cipher)
        {
            //Сериализуем данные в Json
            string messageString = JsonConvert.SerializeObject(messageObject);

            //Записываем логи и историю
            Console.WriteLine(messageString);
            File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", messageString + "\r\n");
            history += messageString;

            //Формируем пакет
            Packet packet = new Packet();
            packet.commanda = commanda;
            packet.cipher = cipher;

            //Перебираем всех клиентов...
            for (int k = 0; k < clients.Count; k++)
            {
                //Зашифровываем данные
                packet.data = Encrypt(messageString, clients[k].user.key, cipher);

                //Сериализуем пакет в Json
                string packetString = JsonConvert.SerializeObject(packet);
                //Кодируем Json в массив байт
                byte[] packetBytes = Encoding.GetEncoding(866).GetBytes(packetString);

                //Отправляем массив байт клиенту
                clients[k].socket.Send(packetBytes);
            }
        }

        private static void Send(string commanda, object messageObject, Packet packetSourse)
        {
            //Сериализуем данные в Json
            string messageString = JsonConvert.SerializeObject(messageObject);

            //Записываем логи и историю
            Console.WriteLine(messageString);
            File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", messageString + "\r\n");
            history += messageString;

            //Формируем пакет
            Packet packet = new Packet();
            packet.commanda = commanda;
            packet.cipher = packetSourse.cipher;
            packet.timeSend = packetSourse.timeSend;

            //Перебираем всех клиентов...
            for (int k = 0; k < clients.Count; k++)
            {
                //Зашифровываем данные
                packet.data = Encrypt(messageString, clients[k].user.key, packet.cipher);

                //Сериализуем пакет в Json
                string packetString = JsonConvert.SerializeObject(packet);
                //Кодируем Json в массив байт
                byte[] packetBytes = Encoding.GetEncoding(866).GetBytes(packetString);

                //Отправляем массив байт клиенту
                clients[k].socket.Send(packetBytes);
            }
        }

        /// <summary>
        /// Отправка пакета определённому клиенту
        /// </summary>
        /// <param name="commanda"></param>
        /// <param name="client"></param>
        private static void Send(string commanda, Client client)
        {
            Packet packet = new Packet();
            packet.commanda = commanda;

            string packetString = JsonConvert.SerializeObject(packet);
            byte[] packetBytes = Encoding.GetEncoding(866).GetBytes(packetString);

            client.socket.Send(packetBytes);
        }

        /// <summary>
        /// Отправка пакета определённому клиенту
        /// </summary>
        /// <param name="commanda"></param>
        /// <param name="messageObject"></param>
        /// <param name="client"></param>
        private static void Send(string commanda, object messageObject, Client client)
        {
            string messageString = JsonConvert.SerializeObject(messageObject);
            byte[] messageBytes = Encoding.GetEncoding(866).GetBytes(messageString);

            Packet packet = new Packet();
            packet.commanda = commanda;
            packet.data = messageBytes;

            string packetString = JsonConvert.SerializeObject(packet);
            byte[] packetBytes = Encoding.GetEncoding(866).GetBytes(packetString);

            client.socket.Send(packetBytes);
        }

        /// <summary>
        /// Выбор локальной конечной точки, к которой будет привязан сервер в локальной сети
        /// </summary>
        /// <returns></returns>
        private static IPHostEntry getIpHost()
        {
            Console.Write(
                "Выберите локальную конечную точку для сокета:\r\n" +
                "1. 127.0.0.1\r\n" +
                "2. 192.168.0.101\r\n" +
                "3. 192.168.0.105\r\n" +
                "или введите вручную\r\n");

            string ipHostString = Console.ReadLine();
            IPHostEntry ipHost = new IPHostEntry();

            switch (ipHostString)
            {
                case "1":
                    ipHost = Dns.GetHostEntry("127.0.0.1");
                    break;
                case "2":
                    ipHost = Dns.GetHostEntry("192.168.0.101");
                    break;
                case "3":
                    ipHost = Dns.GetHostEntry("192.168.0.105");
                    break;
                default:
                    ipHost = Dns.GetHostEntry(ipHostString);
                    break;
            }

            return ipHost;
        }


    }
}