#ifndef LOGGER_HPP
#define LOGGER_HPP

#include <fstream>
#include <QString>

class Logger
{
public:
    static void open(const QString& logFile);
    static void close();
    static void write(const QString& message);
private:
    Logger();
    std::ofstream stream;
    static Logger instance;
};

#endif
