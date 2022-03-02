#include <QDir>
#include <QFileDialog>
#include <QImage>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QStandardPaths>
#include "builder.hpp"
#include "emote_builder.hpp"
#include "logger.hpp"
#include "max_rects_bin_pack.hpp"

Builder::Builder(int atlasWidth, int atlasHeight, int maxAllowedAtlasCount, bool allowOptimizeSize, bool forceSquare, bool allowRotation, QObject* parent) : QObject(parent)
{
    this->atlasWidth = atlasWidth;
    this->atlasHeight = atlasHeight;
    this->maxAllowedAtlasCount = maxAllowedAtlasCount;
    this->forceSquare = forceSquare;
    this->allowOptimizeSize = allowOptimizeSize;
    this->allowRotation = allowRotation;
}

// Adds rect into sequence, indexed incrementally
void Builder::addRect(int width, int height)
{
    RectSize rs;
    rs.width = width;
    rs.height = height;
    sourceRects.append(rs);
}

MaxRectsBinPack Builder::findBestBinPacker(int width, int height, QList<RectSize> &currRects, bool &allUsed)
{
    QList<MaxRectsBinPack> binPackers;
    QList<QList<RectSize>> binPackerRects;
    QList<bool> binPackerAllUsed;

    //MaxRectsBinPack.FreeRectChoiceHeuristic[] heuristics = { MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestAreaFit,
    //                                                         MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestLongSideFit,
    //                                                         MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestShortSideFit,
    //                                                         MaxRectsBinPack.FreeRectChoiceHeuristic.RectBottomLeftRule,
    //                                                         MaxRectsBinPack.FreeRectChoiceHeuristic.RectContactPointRule };

    FreeRectChoiceHeuristic heuristics[4] = { RectBestAreaFit,
                                              RectBestLongSideFit,
                                              RectBestShortSideFit,
                                              RectBottomLeftRule,
                                            };

    for (auto heuristic : heuristics)
    {
        MaxRectsBinPack binPacker(width, height, allowRotation);
        QList<RectSize> activeRects(currRects);
        bool activeAllUsed = binPacker.insert(activeRects, heuristic);

        binPackers.append(binPacker);
        binPackerRects.append(activeRects);
        binPackerAllUsed.append(activeAllUsed);
    }

    int leastWastedPixels = std::numeric_limits<int>::max();
    int leastWastedIndex = -1;
    for (int i = 0; i < binPackers.count(); ++i)
    {
        int wastedPixels = binPackers[i].wastedBinArea();
        if (wastedPixels < leastWastedPixels)
        {
            leastWastedPixels = wastedPixels;
            leastWastedIndex = i;
            oversizeTextures = true;
        }
    }

    currRects = binPackerRects[leastWastedIndex];
    allUsed = binPackerAllUsed[leastWastedIndex];
    return binPackers[leastWastedIndex];
}

int Builder::build()
{
    atlases.clear();
    remainingRectIndices.clear();
    QList<bool> usedRect(sourceRects.count());

    int atlasWidth = this->atlasWidth >> alignShift;
    int atlasHeight = this->atlasHeight >> alignShift;

    // Sanity check, can't build with textures larger than the actual max atlas size
    int align = (1 << alignShift) - 1;
    int minSize = std::max(atlasWidth, atlasHeight);
    int maxSize = std::max(atlasWidth, atlasHeight);
    for (RectSize rs : sourceRects)
    {
        int maxDim = (std::max(rs.width, rs.height) + align) >> alignShift;
        int minDim = (std::max(rs.width, rs.height) + align) >> alignShift;

        // largest texture needs to fit in an atlas
        if (maxDim > maxSize || (maxDim <= maxSize && minDim > minSize))
        {
            remainingRectIndices.clear();
            for (int i = 0; i < sourceRects.count(); ++i)
                remainingRectIndices.append(i);
            return remainingRectIndices.count();
        }
    }

    // Start with all source rects, this list will get reduced over time
    QList<RectSize> rects;
    for (RectSize rs : sourceRects)
    {
        RectSize t;
        t.width = (rs.width + align) >> alignShift;
        t.height = (rs.height + align) >> alignShift;
        rects.append(t);
    }

    bool allUsed = false;
    while (allUsed == false && atlases.count() < maxAllowedAtlasCount)
    {
        int numPasses = 1;
        int thisCellW = atlasWidth, thisCellH = atlasHeight;
        bool reverted = false;

        while (numPasses > 0)
        {
            // Create copy to make sure we can scale textures down when necessary
            QList<RectSize> currRects(rects);

//					MaxRectsBinPack binPacker = new MaxRectsBinPack(thisCellW, thisCellH);
//					allUsed = binPacker.Insert(currRects, MaxRectsBinPack.FreeRectChoiceHeuristic.RectBestAreaFit);
            MaxRectsBinPack binPacker = findBestBinPacker(thisCellW, thisCellH, currRects, allUsed);
            float occupancy = binPacker.occupancy();

            // Consider the atlas resolved when after the first pass, all textures are used, and the occupancy > 0.5f, scaling
            // down by half to maintain PO2 requirements means this is as good as it gets
            bool firstPassFull = numPasses == 1 && occupancy > 0.5f;

            // Reverted copes with the case when halving the atlas size when occupancy < 0.5f, the textures don't fit in the
            // atlas anymore. At this point, size is reverted to the previous value, and the loop should accept this as the final value
            if (firstPassFull ||
                (numPasses > 1 && occupancy > 0.5f && allUsed) ||
                reverted || !allowOptimizeSize)
            {
                QList<Entry> atlasEntries;

                for (auto t : binPacker.getMapped())
                {
                    int matchedWidth = 0;
                    int matchedHeight = 0;

                    int matchedId = -1;
                    bool flipped = false;
                    for (int i = 0; i < sourceRects.count(); ++i)
                    {
                        int width = (sourceRects[i].width + align) >> alignShift;
                        int height = (sourceRects[i].height + align) >> alignShift;
                        if (!usedRect[i] && width == t.width && height == t.height)
                        {
                            matchedId = i;
                            matchedWidth = sourceRects[i].width;
                            matchedHeight = sourceRects[i].height;
                            break;
                        }
                    }

                    // Not matched anything yet, so look for the same rects rotated
                    if (matchedId == -1)
                    {
                        for (int i = 0; i < sourceRects.count(); ++i)
                        {
                            int width = (sourceRects[i].width + align) >> alignShift;
                            int height = (sourceRects[i].height + align) >> alignShift;
                            if (!usedRect[i] && width == t.height && height == t.width)
                            {
                                matchedId = i;
                                flipped = true;
                                matchedWidth = sourceRects[i].height;
                                matchedHeight = sourceRects[i].width;
                                break;
                            }
                        }
                    }

                    // If this fails its a catastrophic error
                    usedRect[matchedId] = true;
                    Entry newEntry;
                    newEntry.flipped = flipped;
                    newEntry.x = t.x << alignShift;
                    newEntry.y = t.y << alignShift;
                    newEntry.w = matchedWidth;
                    newEntry.h = matchedHeight;
                    newEntry.index = matchedId;
                    atlasEntries.append(newEntry);
                }

                Data currAtlas;
                currAtlas.width = thisCellW << alignShift;
                currAtlas.height = thisCellH << alignShift;
                currAtlas.occupancy = binPacker.occupancy();
                currAtlas.entries = atlasEntries;

                atlases.append(currAtlas);

                rects = currRects;
                break; // done
            }
            else
            {
                if (!allUsed)
                {
                    if (forceSquare)
                    {
                        thisCellW *= 2;
                        thisCellH *= 2;
                    }
                    else
                    {
                        // Can only try another size when it already has been scaled down for the first time
                        if (thisCellW < atlasWidth || thisCellH < atlasHeight)
                        {
                            // Tried to scale down, but the texture doesn't fit, so revert previous change, and
                            // iterate over the data again forcing a pass even though there is wastage
                            if (thisCellW < thisCellH) thisCellW *= 2;
                            else thisCellH *= 2;
                        }
                    }

                    reverted = true;
                }
                else
                {
                    if (forceSquare)
                    {
                        thisCellH /= 2;
                        thisCellW /= 2;
                    }
                    else
                    {
                        // More than half the texture was unused, scale down by one of the dimensions
                        if (thisCellW < thisCellH) thisCellH /= 2;
                        else thisCellW /= 2;
                    }
                }

                numPasses++;
            }
        }
    }

    remainingRectIndices.clear();
    for (int i = 0; i < usedRect.count(); ++i)
    {
        if (!usedRect[i])
        {
            remainingRectIndices.append(i);
        }
    }

    return remainingRectIndices.count();
}

inline void swap(QJsonValueRef valueA, QJsonValueRef valueB)
{
    QJsonValue temp(valueA);
    valueA = QJsonValue(valueB);
    valueB = temp;
}

void Builder::rebuild()
{
    for (QImage image : EmoteBuilder::frames)
    {
        addRect(image.width(), image.height());
    }

    Logger::write("Starting build...");
    build();

    Logger::write("Starting rebuild...");

    QJsonArray entries;
    for (int atlasIndex = 0; atlasIndex < atlases.count(); atlasIndex++)
    {
        QImage tex(atlases[atlasIndex].width, atlases[atlasIndex].height, QImage::Format_ARGB32);

        for (int yy = 0; yy < tex.height(); ++yy)
        {
            for (int xx = 0; xx < tex.width(); ++xx)
            {
                tex.setPixelColor(xx, yy, QColor(Qt::transparent));
            }
        }
        for (int i = 0; i < atlases[atlasIndex].entries.count(); ++i)
        {
            Entry entry = atlases[atlasIndex].entries[i];
            QImage source = EmoteBuilder::frames.values()[entry.index];

            QJsonObject frameJson;
            frameJson.insert("flipped", entry.flipped);
            frameJson.insert("h", entry.h);
            frameJson.insert("index", entry.index);
            frameJson.insert("w", entry.w);
            frameJson.insert("x", entry.x);
            frameJson.insert("y", entry.y);
            entries.append(frameJson);

            if (!entry.flipped)
            {
                for (int y = 0; y < source.height(); ++y)
                {
                    for (int x = 0; x < source.width(); ++x)
                    {
                        tex.setPixelColor(entry.x + x, tex.height() - entry.h - entry.y + y, source.pixelColor(x, y));
                    }
                }
            }
            else
            {
                for (int y = 0; y < source.height(); ++y)
                {
                    for (int x = 0; x < source.width(); ++x)
                    {
                        tex.setPixelColor(entry.x + y, tex.height() - entry.h - entry.y + x, source.pixelColor(x, source.height() - y - 1));
                    }
                }
            }
        }

        Logger::write("Saving texture...");
        QString savePath = QFileDialog::getSaveFileName(Q_NULLPTR, "Save atlas texture", Q_NULLPTR, ".png");
        if (savePath.isEmpty()) return;
        savePath += savePath.endsWith(".png") ? "" : ".png";
        tex.save(savePath, "PNG", 100);

        std::sort(entries.begin(), entries.end(), [](const QJsonValue &valueA, const QJsonValue &valueB)
        {
            return valueA.toObject()["index"].toInt() < valueB.toObject()["index"].toInt();
        });
        QJsonObject atlasJson;
        QJsonArray anchors;
        for (auto anchor : EmoteBuilder::anchors)
        {
            QJsonObject anchorObj;
            anchorObj.insert("x", anchor.x());
            anchorObj.insert("y", anchor.y());
            anchors.append(anchorObj);
        }
        atlasJson.insert("anchors", anchors);
        atlasJson.insert("entries", entries);
        atlasJson.insert("fps", EmoteBuilder::fps);
        QJsonDocument jsonDocument(atlasJson);
        QFile jsonFile(savePath + "/../data.json");
        jsonFile.open(QFile::WriteOnly);
        jsonFile.write(jsonDocument.toJson());
    }
}

void Builder::run()
{
    rebuild();
}
