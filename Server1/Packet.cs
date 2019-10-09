using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace Server1
{
    /// <summary>
    /// Кастомный класс пакета
    /// </summary>
    class Packet
    {
        public string commanda;
        public string cipher;
        public byte[] data;
    }

    /// <summary>
    /// Класс, содержащий информацию о файле
    /// </summary>
    class FileInfoKratko
    {
        public string name;
        public long size;
    }

    /// <summary>
    /// Класс, содержащий информацию о клиенте и сокет клиента
    /// </summary>
    class Client {
        public Socket socket;
        public User user;
    }

    /// <summary>
    /// Класс, содержащий информацию о клиенте
    /// </summary>
    class ClientInfo
    {
        //Удалить
        public User user;
        public byte[] key;
    }
}
