#Copyright (c) Microsoft. All rights reserved.
#Licensed under the MIT license. See LICENSE file in the project root for full license information.

cmake_minimum_required(VERSION 2.8.11)
project(azure_iot_sdks)

#making a nice global variable to know if we are on linux or not.
if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    set(LINUX TRUE)
	#on Linux, enable valgrind
	#these commands (MEMORYCHECK...) need to apear BEFORE include(CTest) or they will not have any effect
	find_program(MEMORYCHECK_COMMAND valgrind)	
	set(MEMORYCHECK_COMMAND_OPTIONS "--trace-children=yes --leak-check=full --error-exitcode=1")
endif()

include (CTest)

#the following variables are project-wide and can be used with cmake-gui
option(use_amqp "set use_amqp to ON if amqp is to be used, set to OFF to not use amqp" ON)
option(use_http "set use_http to ON if http is to be used, set to OFF to not use http" ON)
option(use_mqtt "set use_mqtt to ON if mqtt is to be used, set to OFF to not use mqtt" ON)
option(run_e2e_tests "set run_e2e_tests to ON to run e2e tests (default is OFF) [if possible, they are always build]" OFF)
option(use_wsio "set use_wsio to ON if WebSockets is to be used, set to OFF to not use WebSockets" OFF)
option(run_longhaul_tests "set run_longhaul_tests to ON to run longhaul tests (default is OFF)[if possible, they are always build]" OFF)
option(skip_unittests "set skip_unittests to ON to skip unittests (default is OFF)[if possible, they are always build]" OFF)
option(compileOption_C "passes a string to the command line of the C compiler" OFF)
option(compileOption_CXX "passes a string to the command line of the C++ compiler" OFF)
option(build_python "builds the Python native iothub_client module" OFF)
option(build_javawrapper "builds the native iothub_client library for java C wrapper" OFF)


#Use solution folders. 
set_property(GLOBAL PROPERTY USE_FOLDERS ON)

if(IN_OPENWRT)
    ADD_DEFINITIONS("$ENV{TARGET_LDFLAGS}" "$ENV{TARGET_CPPFLAGS}" "$ENV{TARGET_CFLAGS}")
    INCLUDE_DIRECTORIES("$ENV{TOOLCHAIN_DIR}/usr/include" "$ENV{TARGET_LDFLAGS}" "$ENV{TARGET_CPPFLAGS}" "$ENV{TARGET_CFLAGS}")
endif()

if (LINUX)
    if (CMAKE_C_COMPILER_ID STREQUAL "GNU")
        if((NOT "${build_python}" STREQUAL "OFF") OR ("${build_javawrapper}" STREQUAL "ON"))
            # now all static libraries use PIC flag for Python shared lib
            set(CMAKE_C_FLAGS "-fPIC ${CMAKE_C_FLAGS}")
            set(CMAKE_CXX_FLAGS "-fPIC ${CMAKE_CXX_FLAGS}")
        endif()
    endif()
endif()


#if any compiler has a command line switch called "OFF" then it will need special care
if(NOT "${compileOption_C}" STREQUAL "OFF")
    set(CMAKE_C_FLAGS "${compileOption_C} ${CMAKE_C_FLAGS}")
endif()

if(NOT "${compileOption_CXX}" STREQUAL "OFF")
    set(CMAKE_CXX_FLAGS "${compileOption_CXX} ${CMAKE_CXX_FLAGS}")
endif()


add_subdirectory(azure-c-shared-utility)
add_subdirectory(azure-uamqp-c)
add_subdirectory(azure-umqtt-c)

enable_testing()


include_directories(${SHARED_UTIL_INC_FOLDER}/azure_c_shared_utility)

#this project uses several other projects that are build not by these CMakeFiles
#this project also targets several OSes

function(linkUAMQP whatExecutableIsBuilding)
    include_directories(${UAMQP_INC_FOLDER})
    
    if(WIN32)
        #windows needs this define
        add_definitions(-D_CRT_SECURE_NO_WARNINGS)
        add_definitions(-DGB_MEASURE_MEMORY_FOR_THIS -DGB_DEBUG_ALLOC)

        target_link_libraries(${whatExecutableIsBuilding} uamqp aziotsharedutil ws2_32 secur32)

        if(${use_openssl} OR ${use_wsio})
            target_link_libraries(${whatExecutableIsBuilding} $ENV{OpenSSLDir}/lib/ssleay32.lib $ENV{OpenSSLDir}/lib/libeay32.lib)
        
            file(COPY $ENV{OpenSSLDir}/bin/libeay32.dll DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/Debug)
            file(COPY $ENV{OpenSSLDir}/bin/ssleay32.dll DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/Debug)
        endif()
    else()
        target_link_libraries(${whatExecutableIsBuilding} uamqp aziotsharedutil ssl crypto)
    endif()
    
    if(${use_wsio})
        target_link_libraries(${whatExecutableIsBuilding} websockets)
    endif()
endfunction(linkUAMQP)

function(includeMqtt)
    include_directories(${MQTT_INC_FOLDER})
endfunction(includeMqtt)

function(linkMqttLibrary whatExecutableIsBuilding)
    includeMqtt()
    target_link_libraries(${whatExecutableIsBuilding} umqtt)
endfunction(linkMqttLibrary)

function(includeHttp)
endfunction(includeHttp)

function(linkHttp whatExecutableIsBuilding)
    includeHttp()
    if(WIN32)
        if(WINCE)
              target_link_libraries(${whatExecutableIsBuilding} crypt32.lib)
          target_link_libraries(${whatExecutableIsBuilding} ws2.lib)
        else()
            target_link_libraries(${whatExecutableIsBuilding} winhttp.lib)
        endif()
    else()
        target_link_libraries(${whatExecutableIsBuilding} curl)
    endif()
endfunction(linkHttp)

function(linkSharedUtil whatIsBuilding)
    target_link_libraries(${whatIsBuilding} aziotsharedutil)
endfunction(linkSharedUtil)

macro(compileAsC99)
  if (CMAKE_VERSION VERSION_LESS "3.1")
    if (CMAKE_C_COMPILER_ID STREQUAL "GNU")
      set (CMAKE_C_FLAGS "--std=c99 ${CMAKE_C_FLAGS}")
    if (NOT IN_OPENWRT)
      set (CMAKE_CXX_FLAGS "--std=c++11 ${CMAKE_CXX_FLAGS}")
    endif()
    endif()
  else()
    set (CMAKE_C_STANDARD 99)
    set (CMAKE_CXX_STANDARD 11)
  endif()
endmacro(compileAsC99)

macro(compileAsC11)
  if (CMAKE_VERSION VERSION_LESS "3.1")
    if (CMAKE_C_COMPILER_ID STREQUAL "GNU")
      set (CMAKE_C_FLAGS "--std=c11 ${CMAKE_C_FLAGS}")
      set (CMAKE_C_FLAGS "-D_POSIX_C_SOURCE=200112L ${CMAKE_C_FLAGS}")
      set (CMAKE_CXX_FLAGS "--std=c++11 ${CMAKE_CXX_FLAGS}")
    endif()
  else()
    set (CMAKE_C_STANDARD 11)
    set (CMAKE_CXX_STANDARD 11)
  endif()
endmacro(compileAsC11)

if(WIN32)
    set(LOCK_C_FILE ${SHARED_UTIL_ADAPTER_FOLDER}/lock_c11.c)
    set(THREAD_C_FILE ${SHARED_UTIL_ADAPTER_FOLDER}/threadapi_c11.c)
else()
    set(LOCK_C_FILE ${SHARED_UTIL_ADAPTER_FOLDER}/lock_pthreads.c)
    set(THREAD_C_FILE ${SHARED_UTIL_ADAPTER_FOLDER}/threadapi_pthreads.c)
endif()

if(${run_e2e_tests})
    add_subdirectory(testtools)
endif()

add_subdirectory(iothub_client)
add_subdirectory(serializer)
if(NOT "${build_python}" STREQUAL "OFF")
    add_subdirectory(../python/device/iothub_client_python python)
endif()

if("${build_javawrapper}" STREQUAL "ON")	
     add_subdirectory(../javawrapper/device/iothub_client_javawrapper javawrapper)
endif()


