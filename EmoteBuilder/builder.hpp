#ifndef BUILDER_HPP
#define BUILDER_HPP

#include <QObject>
#include <QRunnable>
#include "max_rects_bin_pack.hpp"

class Entry
{
public:
    int             index, x, y, w, h;
    bool            flipped;
};

class Data
{
public:
    int             width, height;
    float           occupancy;
    QList<Entry>    entries;

    Entry findEntryWithIndex(int index)
    {
        for (Entry entry : entries)
        {
            if (index == entry.index)
            {
                return entry;
            }
        }

        return *new Entry();
    }
};

class Builder : public QObject, public QRunnable
{
    Q_OBJECT
public:
    Builder(int atlasWidth, int atlasHeight, int maxAllowedAtlasCount, bool allowOptimizeSize, bool forceSquare, bool allowRotation, QObject* parent = Q_NULLPTR);
    void addRect(int width, int height);
    MaxRectsBinPack findBestBinPacker(int width, int height, QList<RectSize> &currRects, bool &allUsed);
    int build();
    void rebuild();
    void run() override;

private:
    int             maxAllowedAtlasCount = 0;
    int             atlasWidth = 0;
    int             atlasHeight = 0;
    bool            forceSquare = false;
    bool            allowOptimizeSize = true;
    int             alignShift = 0;
    bool            allowRotation = true;

    QList<RectSize> sourceRects;

    QList<Data>     atlases;
    QList<int>      remainingRectIndices;

    bool            oversizeTextures = false;
};

#endif // BUILDER_HPP
