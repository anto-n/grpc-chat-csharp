using Grpc.Core;
using grpc_chat_csharp;
using System.Collections.Specialized;
using System.Text;

namespace Client {
    class GrpcChat {
        Channel channel;
        grpc_chat_csharp.GrpcChat.GrpcChatClient client;
        OrderedDictionary rooms = new OrderedDictionary();
        User user;
        SortedDictionary<UInt32, UInt32> lastPulledMessageIndex = new SortedDictionary<uint, uint>();
        

        public GrpcChat(string userName) {
            channel = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);
            client = new grpc_chat_csharp.GrpcChat.GrpcChatClient(channel);
            user = new User{
                Name = userName,
                Id = 0
            };
        }

        ~GrpcChat(){
            channel.ShutdownAsync().Wait();
        }

        void GetRooms() {
            Console.WriteLine("Get rooms from server");
            var srv_rooms = client.GetRooms(new grpc_chat_csharp.Empty());
            foreach (var room in srv_rooms.Rooms_)
            {
                rooms.Add(room.Id.Id, room);
            }
        }

        Room GetRoomByName(string name) {
            foreach (Room room in rooms.Values)
            {
                if(room.Name == name) {
                    return room;
                }
            }
            return new Room();
        }

        UInt32 JoinRoom(Room room) {
            Console.WriteLine($"Join room ({room.Name})");
            var joinDefinition = new UserRoomById{
                Id = room.Id.Id,
                User = user
            };

            var joinedRoom = client.JoinRoom(joinDefinition);
            return joinedRoom.Id.Id;
        }

        UInt32 CreateRoom(string name) {
            Console.WriteLine($"Create new room ({name})");
            var createDefinition = new UserRoomByName{
                Name = name,
                User = user
            };

            var createdRoom = client.CreateRoom(createDefinition);
            rooms.Add(createdRoom.Id.Id, createdRoom);
            return createdRoom.Id.Id;
        }

        void LeaveRoom(string name) {
            Console.WriteLine($"Leave room ({name})");
            var room = GetRoomByName(name);
            if (!string.IsNullOrEmpty(room.Name)){
                client.LeaveRoom(new UserRoomById{
                    Id = room.Id.Id,
                    User = user
                });
            }
        }

        UInt32 CreateJoinRoom(string name) {
            GetRooms();
            var room = GetRoomByName(name);
            UInt32 id;
            if ( string.IsNullOrEmpty(room.Name)) {
                id = CreateRoom(name);
            } else {
                Console.WriteLine($"Room ({name}) already exists.");
                id = JoinRoom(room);
            }
            lastPulledMessageIndex.Add(id, 0);
            return id;
        }

        void SendMessage(UInt32 roomId, string message) {
            var chatMessage = new ChatMessage{
                User = user,
                Id = new RoomMessageId {
                    RoomId = new RoomId {
                        Id = roomId
                    }
                },
                Text = message
            };

            client.SendMessage(chatMessage);
        }

        public async Task PullMessages(UInt32 roomId) {
            try {
                var lastPulledMessage = lastPulledMessageIndex[roomId];

                var roomDefinition = new RoomMessageId{
                    RoomId = new RoomId {Id=roomId},
                    MessageId = new MessageId{Id = lastPulledMessage + 1}
                };

                using (var call = client.GetMessages(roomDefinition))
                {
                    var responseStream = call.ResponseStream;
                    while (await responseStream.MoveNext())
                    {
                        ChatMessage message = responseStream.Current;
                        lastPulledMessageIndex[roomId] = message.Id.MessageId.Id;
                        if (message.User.Name == user.Name) { // Do not print messages from "myself"
                            continue;
                        }

                        StringBuilder messageToPrint = new StringBuilder("");
                        messageToPrint.Append(message.User.Name)
                                      .Append(": ")
                                      .Append(message.Text);
                        Console.WriteLine(messageToPrint.ToString());
                    }
                }
            }
            catch (RpcException e)
            {
                Console.WriteLine($"RPC failed {e}"); 
                throw;
            }
        }

        public static async Task PeriodicAsync(Func<Task> action, TimeSpan interval, CancellationToken cancellationToken = default)
        {
            using var timer = new PeriodicTimer(interval);
            while (true)
            {
                await action();
                await timer.WaitForNextTickAsync(cancellationToken);
            }
        }

        static void Main(string[] args)
        {
            try
            {
                var userName = "Tommy";
                if (args.Length > 0) {
                    userName = args[0];
                }
                
                GrpcChat chat = new GrpcChat(userName);
                
                var roomId = chat.CreateJoinRoom("main");

                Task statisticsUploader = PeriodicAsync(async () =>
                {
                    try
                    {
                        await chat.PullMessages(roomId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }, TimeSpan.FromSeconds(1));
                
                while(true) {
                    var text = Console.ReadLine();
                    if (text == "exit" || text == null) {
                        break;
                    }
                    chat.SendMessage(roomId, text);
                }
                
                chat.LeaveRoom("main");
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Exception encountered: {ex}");
            }
        }
    }
}