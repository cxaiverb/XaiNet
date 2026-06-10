
![KDXai](https://github.com/user-attachments/assets/434b59bc-f59d-42ef-9269-a0446a129445)

I made this out of spite towards the default Windows networking taskbar item.


This program adds a new taskbar item.


![nGwRSMcEsI](https://github.com/user-attachments/assets/f6325800-06ac-44bb-9667-4d701ee834aa)

On right click, it gives you 3 options and exit. Option 1 opens the control panels network/adapter options, option 2 opens the windows settings app for network, and option 3 shows all of your adapters basic info.

![XaiNet2_P4ozBJIKtJ](https://github.com/user-attachments/assets/f9932db1-7d90-4fb6-ba43-df18679ae9b2)


There is also a UI that appears when clicking the icon, and this is where most of the updates happen. Its current state looks like this:

![image](https://github.com/user-attachments/assets/8cbb9ff9-b36b-48eb-91f5-ad170eb36ddc)

Each adapter has a live upload/download graph, and the tabs let you dig into the details (IP, status, MAC, gateway, DNS). The tray icon updates to match whatever youre connected to, wifi signal bars, wired, or a vpn version when a tunnel is up.


You can also disable viewing of adapters that you arent interested in!
![image](https://github.com/user-attachments/assets/5fb9e229-26b7-4d13-8d52-15ca28c2a08b)


I added the OpenVPN config support I was planning. You can import your .ovpn files and connect/disconnect them straight from the toolbar, open their logs, and even point a profile at a wifi network so it auto connects whenever you join that one. The OpenVPN button only shows up if you have OpenVPN GUI installed.

The wifi searching/discovery I said was coming is in now too. You can scan for networks, connect (it asks for the password), disconnect, and manage your saved profiles. If a network is hidden or you just know its there, you can add it by name, it handles WEP, WPA2 and 802.1X/Enterprise.

I also wired in Tailscale so it lives right here instead of its own tray app. If you have Tailscale installed you get a Tailscale button with your connection status and this devices Tailscale IP, buttons to connect/disconnect/log out, an exit node picker, and a list of the other devices on your tailnet.

There are a few other things tucked in the options menu, auto start with windows, a Nerd Stats toggle for extra adapter info, a logging option for troubleshooting (it writes a log you can hand me if you ever hit a crash), and Myrkur Mode for when you want everything in Comic Sans.


This is heavily inspired by the Network Manager that comes with KDE.
