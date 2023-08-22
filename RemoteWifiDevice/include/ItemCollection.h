#ifndef ITEM_COLLECTION_H
#define ITEM_COLLECTION_H

/*
    Protocol class for the element conformity of the itemCollection
*/
template <class C>
class ICollectable
{
public:
    virtual ~ICollectable() {}
    virtual C &operator=(const C &) = 0;
};

/**
 * @brief A collection for class objects. Class-objects must conform to the 'ICollectable' interface.
 */
template <class T>
class itemCollection
{
public:
    itemCollection()
        : itemCount(0), _Items(nullptr) {}

    itemCollection(const itemCollection<T> &col)
        : itemCount(0), _Items(nullptr)
    {
        for (unsigned int i = 0; i < col.itemCount; i++)
        {
            this->AddItem(col.GetAt(i));
        }
    }

    ~itemCollection()
    {
        this->Clear();
    }

    /*Get the amount of items in the collection*/
    unsigned int GetCount() const
    {
        return itemCount;
    }

    /**
     * @brief Add an element to the collection
     */
    void AddItem(T* item)
    {
        this->AddItem(*item);
    }

    /**
     * @brief Add an element to the collection
     */
    void AddItem(const T &item)
    {
        if (this->itemCount == 0)
        {
            this->_Items = new T *;
            if (this->_Items != nullptr)
            {
                *this->_Items = new T();
                if (*this->_Items != nullptr)
                {
                    **this->_Items = item;
                    this->itemCount++;
                }
            }
        }
        else if (this->itemCount == 1)
        {
            // save
            T *firstItem = *_Items;

            // delete old
            delete this->_Items;

            // create new
            this->_Items = new T *[2];
            if (this->_Items != nullptr)
            {
                // set first item
                this->_Items[0] = firstItem;

                // copy and set the new item
                this->_Items[1] = new T();
                if (this->_Items[1] != nullptr)
                {
                    *this->_Items[1] = item;

                    // increase counter
                    this->itemCount++;
                }
            }
        }
        else
        {
            // save temporary
            T **holder = new T *[this->itemCount];
            if (holder != nullptr)
            {
                for (unsigned int i = 0; i < this->itemCount; i++)
                {
                    holder[i] = this->_Items[i];
                }

                // delete old
                delete[] this->_Items;

                // generate new
                this->_Items = new T *[(this->itemCount + ((unsigned int)1))];
                if (this->_Items != nullptr)
                {
                    // backup to array
                    for (unsigned int i = 0; i < this->itemCount; i++)
                    {
                        this->_Items[i] = holder[i];
                    }

                    // copy and set new item
                    this->_Items[this->itemCount] = new T();

                    if (this->_Items[this->itemCount] != nullptr)
                    {
                        *this->_Items[this->itemCount] = item;

                        // increase counter
                        this->itemCount++;
                    }
                }
                // delete holder
                delete[] holder;
            }
        }
    }

    /**
     * @brief Watch out: if the collection contains no items the access via GetAt(...) will cause an exception
     * -> call GetCount( ) first to check the size of content
     */
    T &GetAt(unsigned int index) const
    {
        return *this->_Items[index];
    }

    /**
     * @brief Insert an element in the collection at the specified index
     */
    void InsertAt(unsigned int index, const T &item)
    {
        if ((index >= 0) && (index < this->itemCount))
        {
            int initialItemCount = this->itemCount;

            // at first take the last item and add it as a new item

            /*
                NOTE: This is a security copy - here a reference of an item out of the collection is taken and used as the input parameter for the AddItem method.
                Since this method clears the original data, the element is deleted before it can be added - look at the method AddItem for more info!
            */
            T tempItem = this->GetAt(itemCount - 1);

            this->AddItem(
                tempItem);

            // if there were more than 1 initial item, realign all items after the requested insertion index (excluding the last one)
            if (initialItemCount > 1)
            {
                for (int i = (initialItemCount - 1); i > ((int)index); i--)
                {
                    this->ReplaceAt(
                        i,
                        this->GetAt(i - 1));
                }
            }
            // insert the item
            this->ReplaceAt(index, item);
        }
        else if (index == this->itemCount)
        {
            // if the insertion-index is on the end of the collection, we only have to add it on the end of the collection
            this->AddItem(item);
        }
    }

    /**
     * @brief Replace an element in the collection at the specified index
     */
    void ReplaceAt(unsigned int index, const T &item)
    {
        if (index < this->itemCount && index >= 0)
        {
            delete this->_Items[index];
            this->_Items[index] = new T();
            *this->_Items[index] = item;
        }
    }

    /**
     * @brief Remove an element from the collection at the specified index
     */
    void RemoveAt(unsigned int index)
    {
        if (index < this->itemCount)
        {
            if (this->itemCount == 1)
            {
                // delete all
                delete *this->_Items;
                delete this->_Items;

                // no items anymore
                this->itemCount = 0;
            }
            else if (this->itemCount == 2)
            {
                // save remaining item
                T *sItem = (index == 0) ? this->_Items[1] : this->_Items[0];

                // delete old
                delete this->_Items[index];
                delete[] this->_Items;

                // create new ptr and set remaining item
                this->_Items = new T *;
                if (this->_Items != nullptr)
                {
                    *this->_Items = sItem;

                    // one item remains
                    this->itemCount = 1;
                }
                else
                {
                    // if the allocation fails, mark the collection as empty
                    this->itemCount = 0;
                }
            }
            else
            {
                // save remaining items
                unsigned int aCnt = 0;
                T **holder = new T *[this->itemCount - ((unsigned int)1)];
                if (holder != nullptr)
                {
                    for (unsigned int i = 0; i < this->itemCount; i++)
                    {
                        if (i != index)
                        {
                            holder[aCnt] = this->_Items[i];
                            aCnt++;
                        }
                    }

                    // delete old
                    delete this->_Items[index];
                    delete this->_Items;

                    // create new
                    this->_Items = new T *[itemCount - ((unsigned int)1)];
                    if (this->_Items != nullptr)
                    {
                        // set remaining items
                        for (unsigned int i = 0; i < (this->itemCount - ((unsigned int)1)); i++)
                        {
                            this->_Items[i] = holder[i];
                        }
                        // decrease counter
                        this->itemCount--;
                    }
                    delete[] holder;
                }
            }
        }
    }

    /* Clear all elements in the collection*/
    void Clear()
    {
        if (this->itemCount > 0)
        {
            for (unsigned int i = 0; i < this->itemCount; i++)
            {
                delete this->_Items[i];
            }
            if (this->itemCount > 1)
            {
                delete[] this->_Items;
            }
            else
            {
                delete this->_Items;
            }
            this->itemCount = 0;
        }
    }

    itemCollection<T> &operator=(const itemCollection<T> &col)
    {
        this->Clear();

        for (unsigned int i = 0; i < col.itemCount; i++)
        {
            this->AddItem(col.GetAt(i));
        }
        return *this;
    }

    void operator+=(const T &item)
    {
        this->AddItem(item);
    }

    T operator[](unsigned int position) const
    {
        return this->GetAt(position);
    }

    /**
     * @brief This method returns a pointer to the allocated memory of the collection item at the specified index.
     *	This gives direct access to the core data.
     *  !USE WITH CAUTION! If used in the wrong way this method will corrupt the collection! Do not delete the element!
     */
    T *getObjectCoreReferenceAt(unsigned int index)
    {
        if (index < this->itemCount)
        {
            return this->_Items[index];
        }
        else
        {
            return nullptr;
        }
    }

private:
    unsigned int itemCount;
    T **_Items;
};

#endif