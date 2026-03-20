@echo off
adb forward tcp:5005 tcp:5005
echo Port forwarding set up: PC:5005 -> Quest:5005
pause