using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.IO;
//using System.Diagnostics;

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;
using System.Media;

namespace SocketServer
{
    class Program
    {
        static Socket sListener;
        static System.Collections.Generic.List<Socket> clients = new System.Collections.Generic.List<Socket>();

        static Thread Thread1;

        static string history = "";
        static System.Collections.Generic.List<String> logins = new System.Collections.Generic.List<string>();

        static byte[] ping = new byte[1] { 0 }; //Синхронизация

        static void Main(string[] args)
        {
            Console.Write("[" + DateTime.Now.ToString("HH:mm:ss") + "] Лог сервера:\r\n");

            Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\Логи");
            Directory.CreateDirectory(Directory.GetCurrentDirectory() + "\\Выгрузки");
            File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", "[" + DateTime.Now.ToString("HH:mm:ss") + "] Лог сервера:\r\n");
            
            // Устанавливаем для сокета локальную конечную точку
            IPHostEntry ipHost = Dns.GetHostEntry("192.168.0.101");
            //IPHostEntry ipHost = Dns.GetHostEntry("127.0.0.1");
            IPAddress ipAddr = ipHost.AddressList.FirstOrDefault((a) => a.AddressFamily == AddressFamily.InterNetwork);
            IPEndPoint ipEndPoint = new IPEndPoint(ipAddr, 1001);

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
                string data = null;
                byte[] bytes = new byte[1024];
                int bytesRec = 0;
                //Console.WriteLine("\r\nОжидаем соединение через порт {0}", ipEndPoint);

                for (int i = 0; i < clients.Count; i++)
                {

                    if (!clients[i].Poll(100, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    try
                    {
                        bytesRec = clients[i].Receive(bytes);
                        // Показываем данные на консоли
                        data = Encoding.UTF8.GetString(bytes, 0, bytesRec);
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
                                            Send("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + logins[i] + " : " + data);
                                        }
                                    }
                                }
                                break;
                        }
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

        static string loginsMsg()
        {
            string text = "|";
            for (int k = 0; k < logins.Count; k++)
            {
                text += logins[k] + "|";
            }
            return text;
        }

        static string fileMsg()
        {
            string text = "|";
            string[] files = Directory.GetFiles(Directory.GetCurrentDirectory() + "\\Выгрузки");
            FileInfo fileInfos;
            for (int k = 0; k < files.Length; k++)
            {
                fileInfos = new FileInfo(files[k]);
                text += Path.GetFileName(files[k]) + "|" + fileInfos.Length + "|";
            }
            return text;
        }

        static void Send(string message)
        {
            byte[] msg = Encoding.UTF8.GetBytes(message);

            Console.Write(message);
            File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", message);
            history += message;

            for (int k = 0; k < clients.Count; k++)
            {
                try
                {
                    clients[k].Send(msg);
                }
                catch (Exception ex)
                {
                    Console.Write(ex.Message + "\r\n" + ex.StackTrace + "\r\n");
                    File.AppendAllText(Directory.GetCurrentDirectory() + "\\Логи\\" + DateTime.Now.ToString("dd.MM.yyyy") + ".txt", ex.Message + "\r\n" + ex.StackTrace + "\r\n");
                }
            }
        }
    }
}