# Amaurot - FFXIV Map Editor

A work in progress tool for visualizing and navigating Final Fantasy XIV map data, territories, quests, NPCs, and game content.

<img src="https://github.com/user-attachments/assets/d6a5dd69-5e02-4f28-8fe7-d9ea18e75423" alt="Map Editor Interface" width="900">

## Features

Development is ongoing, but the current version includes:

### **Interactive Map Visualization**
- **High-resolution map display** with zoom and pan controls
- **Dynamic marker system** for NPCs, quests, FATEs, and points of interest
- **Territory navigation** 
- **Real-time coordinate display** 
- **Customizable marker visibility** (Aetherytes, NPCs, Shops, Landmarks, etc.)

### **Quest & Content Management**
- **Quest browser** with location data and details
- **Quest navigation** - double-click to jump to quest locations
- **Instance content viewer** with dungeon/trial information
- **FATE displays** across all territories
- **NPC quest associations** showing which NPCs offer specific quests
- **🆕 Quest Battle system** - Comprehensive quest battle location tracking and script management

### **🆕 Quest Battle Features**
- **Quest Battle discovery** - Automatically extracts quest battle locations from LGB files
- **Quest Battle script integration** - Links quest battles to Sapphire Server scripts
- **Quest battle markers** on maps with dedicated icons
- **Quest Battle details window** with script editing capabilities
- **Dual data sources** - Loads from both LGB files and Sapphire repository scripts
- **Advanced script management** - Open quest battle scripts in VS Code or Visual Studio

### **NPC & Entity Data**
- **Comprehensive NPC database** with positions and associated quests
- **Interactive NPC markers** - click to view quest details
- **Territory-filtered NPC lists** showing relevant NPCs for current map

### **Integrated Tools**
- **LGB Parser integration** - parse FFXIV LGB files directly from the editor
- **Quest file extraction**
- **Multi-file script editing** - Open related C++ and Lua files in single editor windows
- **Script import system** - Import generated quest scripts into Sapphire repository
- **🆕 Performance optimizations** - Async loading and caching for improved responsiveness

### **🆕 Performance Enhancements**
- **Async data loading** - All major data operations run asynchronously to prevent UI freezing
- **Smart caching system** - Territory markers, script information, and LGB data are cached for faster access
- **Background processing** - Heavy operations like Quest Battle extraction run on background threads
- **Optimized coordinate conversion** - Fast LGB-to-map coordinate transformation algorithms
- **Efficient memory management** - Proper disposal of resources and optimized data structures

### **Customization & Settings**
- **Persistent settings** with auto-save functionality
- **Debug mode** with logging and performance metrics
- **Panel layout customization** with dynamic grid reorganization
- **Auto-load game data** option for streamlined startup
- **🆕 Enhanced UI** - Improved list spacing and modern visual elements

### **Search & Filtering**
- **Real-time search** across territories, quests, NPCs, and FATEs
- **Territory deduplication** option for cleaner lists
- **🆕 Enhanced filtering** - Filter by script availability, quest types, and content categories


### **Customization & Settings**
- **Persistent settings** with auto-save functionality
- **Debug mode** with comprehensive logging
- **Panel layout customization** with dynamic grid reorganization
- **Auto-load game data** option for streamlined startup

### **Territories & Maps**
- **Supported** A Realm Reborn & Heavensward
- **most unique territories** with map visualization
- **Overworld zones, dungeons, trials, and raids**

### **🆕 Data Sources & Integration**
- **Multiple data sources** - Saint Coinach, Lumina, and Libra Eorzea database integration
- **LGB file parsing** - Direct extraction from FFXIV LGB files
- **Error handling** - Graceful degradation with comprehensive fallback mechanisms

### Script Management
- Import generated quest scripts into your repository
- Track script availability across your codebase
- Coordinate between different script types (C++, Lua)


## Prerequisites

- **.NET 8.0** Runtime
- **Windows 10/11** (WPF application)
- **Final Fantasy XIV** installation (required for data access)

