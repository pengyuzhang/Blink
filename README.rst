What is Blink?
==============

Blink is a link layer protocol designed for a single hop backscatter sensor networks.
Blink is

1. fast, because it reduces channel probe time by exploiting sharp channel transition.
2. accurate, because its classifier follows optimal bitrate closely.
3. simple, because it obviates frequent channel probe by exploiting sensor mobility patterns.

For more in-depth information please look at the included copy of our MobiSys 2012 paper on Blink.


Blink's Design
==============

Blink consists of four main components: 1) backscatter sensor mobility detector,
2) channel prober to estimate link quality, 3) bit rate selection module,
and 4) channel selection module.

Please have a look at the section 4 of the enclosed paper for the details on each of these components.


System Requirements
===================

Blink runs on a Window XP SP 3 platform. It has the following requirements:

A Impinj Speedway RFID reader.
   Our implementation uses LLRP and Impinj Speedway RFID reader hooks to adjust
   RFID reader physical layer configurations.

Microsoft Visual Studio 2008 or higher.
   Blink source code is compiled with Microsoft Visual Studio 2008.

A ethernet router.
   A ethernet router is needed to connect Impinj Speedway RFID reader to your computer.
   The default reader connection IP is 10.0.0.200. You need to change this configuration
   based on the DHCP of your ethernet router.



Compliling and running Blink
============================

To compile Blink, run WISP GUI.sln in root directory and set WispDemo as the Startup project.



Evaluating Blink
================

The default rate adaptation algorithm (AutoSet) on Impinj Speedway RFID reader can be run
via web interface. Here is a tutorial on how to run Impinj Speedway RFID reader via web
interface: http://wisp.wikispaces.com/UsingImpinjWebInterface. You can compare the throughput
achieved by AutoSet and Blink.


Code Overview
===============

Blink is written as a C# userspace code. Its implementation can be found in ReaderLibrary/ directory.
It runs as a single thread and timeouts to switch among mobility detection mode,
channel probe mode and transmission mode. loss_fast_probe.dat and rssi_fast_probe.dat contain
the trained link metrics used in classification.

The details of the functions in each mode are shown below:

Sensor mobility detection when Blink boots
------------------------------------------

* ``ProbeMobilityPatterns()``: record the link signatures in the first sensor scan
* ``RecordMobilityPatterns()``: record the link signatures in the second sensor scan
* ``CheckTagMobility()``: identify stationary or mobile sensors

Static Sensor Inventory
-----------------------

* ``RateAdaptation_SlowMovingTag()``: bit-rate selection for stationary sensor. If sensor is still in the same
  location, bit-rate does not change.
* ``SlowMovingTag()``: stationary sensor inventory.
* ``SlowMovingTag_MobilityCheck()``: check whether stationary sensor becomes mobile sensor.

Mobile Sensor Inventory
-----------------------

* ``RateAdaptation_FastMovingTag()``: bit-rate selection for mobile sensor. A new bit-rate is selected every 5s.
* ``FastMovingTag_MobilityCheck()``: check whether mobile sensor becomes stationary sensor.

Questions?
==================
Pengyu Zhang (pyzhang@cs.umass.edu)
