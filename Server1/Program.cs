using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.IO;

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;
using System.Media;
using Server1;
using Newtonsoft.Json;

namespace SocketServer
{
    class Program
    {
        static Socket sListener;
        static List<Socket> clients = new List<Socket>();

        static Thread Thread1;

        static string history = "";
        static List<String> logins = new List<string>();

        static void Main(string[] args)
        {

            Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\Логи");
            Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\Выгрузки");
            File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", "[" + DateTime.Now.ToString("HH:mm:ss") + "] Лог сервера:\r\n");

            // Устанавливаем для сокета локальную конечную точку
            IPHostEntry ipHost = getIpHost();
            IPAddress ipAddr = ipHost.AddressList.FirstOrDefault((a) => a.AddressFamily == AddressFamily.InterNetwork);
            IPEndPoint ipEndPoint = new IPEndPoint(ipAddr, 1001);

            Console.Write("[" + DateTime.Now.ToString("HH:mm:ss") + "] Лог сервера:\r\n");

            // Создаем сокет Tcp/Ip
            sListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Назначаем сокет локальной конечной точке и слушаем входящие сокеты
            try
            {
                Thread1 = new Thread(delegate ()
                {
                    ReceiveAndSend();
                });

                sListener.Bind(ipEndPoint);

                Thread1.Start();

                // Начинаем слушать соединения
                sListener.Listen(10);
                
                while (true)
                {
                    // Программа приостанавливается, ожидая входящее соединение
                    clients.Add(sListener.Accept());
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

        static void ReceiveAndSend()
        {
            while (true)
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    if (!clients[i].Poll(100, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    try
                    {
                        string message;
                        string fileName;

                        Packet packet = GetPacket(clients[i]);

                        switch (packet.commanda)
                        {
                            case "Сообщение":
                                message = Encoding.UTF8.GetString(packet.data);
                                message = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + logins[i] + " : " + message;
                                Send("Сообщение", message);
                                break;
                            case "Подключиться":
                                logins.Add(Encoding.UTF8.GetString(packet.data));
                                message = "К чату подключился: " + logins[i] + " [" + clients[i].RemoteEndPoint + "]\r\n";
                                Send("Сообщение", message);
                                Thread.Sleep(1000);
                                ObnovlenieInfo(clients[i]);
                                break;
                            case "Отключиться":
                                message = "От чата отключился: " + logins[i] + "\r\n";
                                Send("Сообщение", message);
                                logins.RemoveAt(i);
                                ObnovlenieInfo(clients[i]);
                                break;
                            case "Синхронизация":
                                //Тупо синхронизация
                                break;
                            case "Файлы":
                                //Походу не надо
                                break;
                            case "История":
                                Send("Сообщение", history, clients[i]);
                                break;
                            case "Выгрузить":
                                {
                                    fileName = Encoding.UTF8.GetString(packet.data);

                                    Send("Синхронизация", clients[i]);

                                    FileStream stream = new FileStream(Directory.GetCurrentDirectory() + "\\Выгрузки\\" + fileName, FileMode.Create, FileAccess.Write);
                                    BinaryWriter f = new BinaryWriter(stream);
                                    byte[] buffer = new byte[8192]; //Буфер для файла
                                    byte[] bFSize = new byte[512]; //Размер файла

                                    int bytesiRec = clients[i].Receive(bFSize); //Принимаем размер
                                    Send("Синхронизация", clients[i]);
                                    int fSize = Convert.ToInt32(Encoding.UTF8.GetString(bFSize, 0, bytesiRec));

                                    int processed = 0; //Байт принято
                                    while (processed < fSize) //Принимаем файл
                                    {
                                        if ((fSize - processed) < 8192)
                                        {
                                            int bytesi = (fSize - processed);
                                            byte[] buf = new byte[bytesi];
                                            bytesi = clients[i].Receive(buf);
                                            f.Write(buf, 0, bytesi);
                                        }
                                        else
                                        {
                                            int bytesi = clients[i].Receive(buffer);
                                            f.Write(buffer, 0, bytesi);
                                        }
                                        Send("Синхронизация", clients[i]);
                                        processed += 8192;
                                    }
                                    f.Close();
                                    stream.Close();

                                    Thread.Sleep(1000);
                                    message = logins[i] + " выгрузил на сервер файл: " + fileName + "\r\n";
                                    Send("Сообщение", message);
                                    Thread.Sleep(1000);
                                    /*
                                    Send("!логины" + loginsMsg() + "\r\n");
                                    clients[i].Receive(ping);
                                    */
                                    Send("Файлы", fileMsgStringArray());
                                }
                                break;
                            case "Загрузить":
                                {
                                    fileName = Directory.GetCurrentDirectory() + "\\Выгрузки\\" + Encoding.UTF8.GetString(packet.data);

                                    byte[] ping = new byte[1] { 0 }; //Синхронизация
                                    Send("Загрузить", Path.GetFileName(fileName), clients[i]);

                                    if (Sinhronizatiya(clients[i]))
                                    {
                                        return;
                                    }

                                    FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                                    BinaryReader f = new BinaryReader(stream);
                                    byte[] buffer = new byte[8192]; //Буфер для файла
                                    int bytesi = 8192;
                                    int fSize = Convert.ToInt32(stream.Length);

                                    byte[] bFSize = Encoding.UTF8.GetBytes(Convert.ToString(fSize)); //Размер файла
                                    clients[i].Send(bFSize); //Передаем размер
                                    if (Sinhronizatiya(clients[i]))
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
                                            clients[i].Send(buf);
                                        }
                                        else
                                        {
                                            f.Read(buffer, 0, bytesi);
                                            clients[i].Send(buffer);
                                        }

                                        if (Sinhronizatiya(clients[i]))
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


                        /*
                        switch (data)
                        {
                            case "!история\r\n":
                                byte[] msg = Encoding.UTF8.GetBytes(history);
                                clients[i].Send(msg);
                                break;
                            case "!отключиться":
                                //handlers[i].Disconnect(false);
                                Send("От чата отключился: " + logins[i] + "\r\n");
                                logins.RemoveAt(i);
                                Send("!логины" + loginsMsg() + "\r\n");
                                clients[i].Receive(ping);
                                Send("!файлы" + fileMsg() + "\r\n");
                                break;
                            default:
                                string log;
                                if ((data.Length > 13) && (data.Substring(0, 13) == "!подключиться"))
                                {
                                    logins.Add(data.Substring(13, data.Length - 13));
                                    log = "К чату подключился: " + logins[i] + " [" + clients[i].RemoteEndPoint + "]\r\n";
                                    Send(log);
                                    Thread.Sleep(1000);
                                    Send("!логины" + loginsMsg() + "\r\n");
                                    clients[i].Receive(ping);
                                    Send("!файлы" + fileMsg() + "\r\n");
                                }
                                else
                                {
                                    if ((data.Length > 10) && (data.Substring(0, 10) == "!выгрузить"))
                                    {
                                        string fileName = data.Substring(10, data.Length - 10);
                                        Send(logins[i] + " выгрузил на сервер файл: " + fileName + "\r\n");

                                        clients[i].Send(ping); // 1

                                        FileStream stream = new FileStream(Directory.GetCurrentDirectory() + "\\Выгрузки\\" + fileName, FileMode.Create, FileAccess.Write);
                                        BinaryWriter f = new BinaryWriter(stream);
                                        byte[] buffer = new byte[8192]; //Буфер для файла
                                        byte[] bFSize = new byte[512]; //Размер файла

                                        int bytesiRec = clients[i].Receive(bFSize); //Принимаем размер
                                        clients[i].Send(ping); // 2
                                        int fSize = Convert.ToInt32(Encoding.UTF8.GetString(bFSize, 0, bytesiRec));

                                        int processed = 0; //Байт принято
                                        while (processed < fSize) //Принимаем файл
                                        {
                                            if ((fSize - processed) < 8192)
                                            {
                                                int bytesi = (fSize - processed);
                                                byte[] buf = new byte[bytesi];
                                                bytesi = clients[i].Receive(buf);
                                                f.Write(buf, 0, bytesi);
                                            }
                                            else
                                            {
                                                int bytesi = clients[i].Receive(buffer);
                                                f.Write(buffer, 0, bytesi);
                                            }
                                            clients[i].Send(ping); // 3
                                            processed += 8192;
                                        }
                                        f.Close();
                                        stream.Close();
                                        Thread.Sleep(1000);
                                        Send("!логины" + loginsMsg() + "\r\n");
                                        clients[i].Receive(ping);
                                        Send("!файлы" + fileMsg() + "\r\n");
                                    }
                                    else
                                    {
                                        if ((data.Length > 10) && (data.Substring(0, 10) == "!загрузить"))
                                        {
                                            string fileName = Directory.GetCurrentDirectory() + "\\Выгрузки\\" + data.Substring(10, data.Length - 12);

                                            byte[] ping = new byte[1] { 0 }; //Синхронизация
                                            byte[] message = Encoding.UTF8.GetBytes("!загрузить" + Path.GetFileName(fileName));
                                            int bytesSent = clients[i].Send(message);
                                            clients[i].Receive(ping); // 1

                                            FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                                            BinaryReader f = new BinaryReader(stream);
                                            byte[] buffer = new byte[8192]; //Буфер для файла
                                            int bytesi = 8192;
                                            int fSize = Convert.ToInt32(stream.Length);

                                            byte[] bFSize = Encoding.UTF8.GetBytes(Convert.ToString(fSize)); //Размер файла
                                            clients[i].Send(bFSize); //Передаем размер
                                            clients[i].Receive(ping); // 2

                                            int processed = 0; //Байт передано
                                            while (processed < fSize) //Передаем файл
                                            {
                                                if ((fSize - processed) < 8192)
                                                {
                                                    bytesi = Convert.ToInt32(fSize - processed);
                                                    byte[] buf = new byte[bytesi];
                                                    f.Read(buf, 0, bytesi);
                                                    clients[i].Send(buf);
                                                }
                                                else
                                                {
                                                    f.Read(buffer, 0, bytesi);
                                                    clients[i].Send(buffer);
                                                }

                                                clients[i].Receive(ping); // 3
                                                processed += 8192;
                                            }
                                            f.Close();
                                            stream.Close();
                                        }
                                        else
                                        {
                                            // Отправляем ответ клиентам
                                            Packet packet = JsonConvert.DeserializeObject<Packet>(data);
                                            string message = Encoding.UTF8.GetString(packet.data);
                                            Send("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + logins[i] + " : " + message);
                                            
                                            //Send("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + logins[i] + " : " + data);
                                        }
                                    }
                                }
                                break;
                        }
                        */
                    }
                    catch (Exception ex)
                    {
                        Console.Write(ex.Message + "\r\n" + ex.StackTrace + "\r\n");
                        File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", ex.Message + "\r\n" + ex.StackTrace + "\r\n");
                        clients.RemoveAt(i);
                    }
                }
            }
        }

        static string[] loginsMsgStringArray()
        {
            string[] result = new string[logins.Count];

            for (int k = 0; k < logins.Count; k++)
            {
                result[k] = logins[k];
            }
            return result;
        }

        static FileInfoKratko[] fileMsgStringArray()
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

        static void ObnovlenieInfo(Socket client)
        {
            Send("Логины", loginsMsgStringArray());

            if (Sinhronizatiya(client))
            {
                Send("Файлы", fileMsgStringArray(), client);
            }
        }

        static bool Sinhronizatiya(Socket client)
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

        static Packet GetPacket(Socket client)
        {
            byte[] bytes = new byte[1024];
            int bytesRec = client.Receive(bytes);
            string data = Encoding.UTF8.GetString(bytes, 0, bytesRec);
            Packet packet = JsonConvert.DeserializeObject<Packet>(data);

            return packet;
        }

        static void Send(string commanda, object messageObject)
        {
            string messageString = JsonConvert.SerializeObject(messageObject);
            byte[] messageBytes = Encoding.UTF8.GetBytes(messageString);

            Console.WriteLine(messageString);
            File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", messageString + "\r\n");
            history += messageString;

            Packet packet = new Packet();
            packet.commanda = commanda;
            packet.data = messageBytes;

            string packetString = JsonConvert.SerializeObject(packet);
            byte[] packetBytes = Encoding.UTF8.GetBytes(packetString);

            for (int k = 0; k < clients.Count; k++)
            {
                clients[k].Send(packetBytes);
            }
        }

        static void Send(string commanda, Socket client)
        {
            Packet packet = new Packet();
            packet.commanda = commanda;

            string packetString = JsonConvert.SerializeObject(packet);
            byte[] packetBytes = Encoding.UTF8.GetBytes(packetString);

            client.Send(packetBytes);
        }

        static void Send(string commanda, object messageObject, Socket client)
        {
            string messageString = JsonConvert.SerializeObject(messageObject);
            byte[] messageBytes = Encoding.UTF8.GetBytes(messageString);

            Packet packet = new Packet();
            packet.commanda = commanda;
            packet.data = messageBytes;

            string packetString = JsonConvert.SerializeObject(packet);
            byte[] packetBytes = Encoding.UTF8.GetBytes(packetString);

            client.Send(packetBytes);
        }

        static IPHostEntry getIpHost()
        {
            Console.Write(
                "Выберите локальную конечную точку для сокета:\r\n" +
                "1. 127.0.0.1\r\n" +
                "2. 192.168.0.101\r\n" +
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
                default:
                    ipHost = Dns.GetHostEntry(ipHostString);
                    break;
            }

            return ipHost;
        }


    }
}