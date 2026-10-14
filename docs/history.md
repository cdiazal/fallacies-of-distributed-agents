# History

Where the ten fallacies come from, and where the sources disagree.

## Deutsch's eight

The eight fallacies of distributed computing come from Sun Microsystems, and James Gosling's site credits
all eight to L. Peter Deutsch ([Gosling](https://nighthacks.com/jag/res/Fallacies.html)).

No first-hand account gives the year often quoted, 1994. A Java Developer's Journal article of January 8,
2004, is titled "Deutsch's Fallacies, 10 Years After," but starts its story in the fall of 1991
([Van Den Hoogen, 2004](https://web.archive.org/web/20070811082651/http://java.sys-con.com/read/38665.htm)).
On Software Engineering Radio in 2021, Deutsch placed his part between late 1991 and early 1993, and
said Wikipedia's dates were wrong
([SE Radio 470, machine-generated transcript](https://se-radio.net/2021/07/episode-470-l-peter-deutsch-on-the-fallacies-of-distributed-computing/)).
Wikipedia still says 1994 ([Wikipedia](https://en.wikipedia.org/wiki/Fallacies_of_distributed_computing)).

Authorship is disputed too. The 2004 article says Bill Joy and Dave Lyon had already listed the
first four, Deutsch took the list to seven, and Gosling added the eighth, *the network is homogeneous*,
around 1997. Deutsch told it differently in 2021: one of the two Lyon brothers, Tom or Dick (he didn't
remember which), came up with the first four, and he added the next four, the eighth among them. Months
before the episode, Dick Lyon, editing Wikipedia, wrote that they came from his brother Tom
([Wikipedia edit, March 13, 2021](https://en.wikipedia.org/w/index.php?title=Fallacies_of_distributed_computing&diff=prev&oldid=1011957846)).
Wikipedia today says Dave.

## A ninth that Deutsch endorsed

In the same episode, the host, Jeff Doolittle, read out a ninth, *we all trust each other*. He said its
origin was unclear, possibly Gosling, and that Deutsch had already told him it belonged on the list.
Deutsch called it an expansion of number four, *the network is secure*, and restated it as an assumption
that the party on the other end is trustworthy
([SE Radio 470, transcript](https://se-radio.net/2021/07/episode-470-l-peter-deutsch-on-the-fallacies-of-distributed-computing/)).
A Wikipedia edit of January 22, 2024, credited this ninth to Deutsch himself, and the article still does
([Wikipedia edit](https://en.wikipedia.org/w/index.php?title=Fallacies_of_distributed_computing&diff=prev&oldid=1197949444)).

## Ted Neward's fallacies of enterprise computing

In May 2004, Ted Neward wrote that he had added two fallacies to Deutsch's eight for his book *Effective
Enterprise Java*, and called the ten the fallacies of enterprise computing. In that post and the next, he
took up one more, suggested by an attendee at a No Fluff Just Stuff symposium: business logic can and
should be centralized
([Neward, May 17, 2004](http://web.archive.org/web/20051107004144/http://neward.net/ted/weblog/index.jsp?date=20040517);
[May 18, 2004](http://web.archive.org/web/20051107004212/http://neward.net/ted/weblog/index.jsp?date=20040518)).
His post of February 19, 2008, lists ten: Deutsch's eight, then that the system is monolithic and that
the system is finished
([Neward, 2008](https://blogs.newardassociates.com/blog/2008/the-fallacies-remain.html)). In August
2016, he published a new list of nine, all three included, and explained that he'd come to see
enterprise fallacies as a separate list, not an add-on to Deutsch's
([Neward, 2016](https://blogs.newardassociates.com/blog/2016/enterprise-computing-fallacies.html)).

## Other lists after the eight

Udi Dahan's Distributed Systems Design Fundamentals course teaches these three after Deutsch's eight,
numbered 9 to 11, but calls the first *the system is atomic*, where Neward's 2008 and 2016 posts say
monolithic. It also rewords five of the eight
([Particular Software](https://learn.particular.net/courses/distributed-systems-design-fundamentals-online)).

Mark Richards and Neal Ford have named three of their own, also numbered 9 to 11:
*versioning is simple*
([Lesson 147, October 23, 2022](https://www.developertoarchitect.com/lessons/lesson147.html)),
*compensating updates always work*
([Lesson 148, November 6, 2022](https://www.developertoarchitect.com/lessons/lesson148.html)), and
*observability is optional*
([Lesson 186, May 6, 2024](https://www.developertoarchitect.com/lessons/lesson186.html)). Lesson 186 and
a podcast of June 26, 2025, give the first as *versioning is easy*
([Thoughtworks](https://www.thoughtworks.com/insights/podcasts/technology-podcasts/three-new-fallacies-distributed-computing)).
Wikipedia lists the three unnumbered, under a sentence that dates them to 2020, and doesn't mention
Neward.

## Two guides to the eight

Arnon Rotem-Gal-Oz's *Fallacies of Distributed Computing Explained* walks through each of the eight, and
Gosling's page links to it. The PDF carries his byline, and its metadata gives the author as arnonrgo and
a creation date of May 28, 2006
([archived PDF](https://web.archive.org/web/20070204070434/http://www.rgoarchitects.com:80/Files/fallacies.pdf)).
Wikipedia instead links a ResearchGate entry with the same title, dated January 2008, under another name.

David Boike wrote one post per fallacy for Particular Software. The first was written on July 6, 2016, as
an excerpt from the book *Dr. Harvey and the 8 Fallacies of Distributed Computing*
([archived copy](https://web.archive.org/web/20160723083627/http://particular.net:80/blog/the-network-is-reliable)).
The first three now show June 4, 2019;
[#4](https://particular.net/blog/the-network-is-secure) to
[#8](https://particular.net/blog/the-network-is-homogeneous) are dated August 20, 2019, to June 29, 2021.

## Why these words, and why ten

This catalog gives Deutsch's eight in the words of Gosling's page, which credits them to Deutsch himself,
not in later rewordings like the course's. For the same reason, its last two entries,
[business logic can and should be centralized](../fallacies/09-logic-can-be-centralized/) and
[the system is monolithic](../fallacies/10-system-is-monolithic/), keep the names in Neward's posts,
monolithic rather than atomic, and are labeled as Ted Neward's fallacies of enterprise computing. After
eight, the numbers already mean three different things, so the 9 and 10 here only number entries. And the
catalog is closed at ten: the extensions so far disagree on what comes after eight, and a closed list
keeps every citation pointing at the same ten.

[← All ten fallacies](../README.md)
