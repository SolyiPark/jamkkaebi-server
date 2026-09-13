package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.friend.dto.response.EraCount;
import com.example.jamkkaebi.friend.dto.response.PlayerCard;
import com.example.jamkkaebi.friend.spi.PlayerCollection;
import com.example.jamkkaebi.friend.spi.PlayerCollectionReader;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.User;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.stereotype.Component;

import java.util.Arrays;
import java.util.Collection;
import java.util.List;
import java.util.Map;

/**
 * 사용자 요약 카드를 만든다.
 *
 * <p>수집률은 유물 도메인이 {@link PlayerCollectionReader} 로 제공한다.
 */
@Component
public class PlayerCardAssembler {

    private final ObjectProvider<PlayerCollectionReader> collectionReader;
    private final GameClock gameClock;

    public PlayerCardAssembler(ObjectProvider<PlayerCollectionReader> collectionReader, GameClock gameClock) {
        this.collectionReader = collectionReader;
        this.gameClock = gameClock;
    }

    // 여러 사용자의 진행도를 한 번에 읽는다.
    public Map<Long, PlayerCollection> collectionsOf(Collection<Long> userIds) {
        PlayerCollectionReader reader = collectionReader.getIfAvailable();
        if (reader == null || userIds.isEmpty()) {
            return Map.of();
        }
        return reader.readAll(userIds);
    }

    public PlayerCard card(User user, Map<Long, PlayerCollection> collections) {
        return new PlayerCard(
                user.getFriendCode(),
                user.getNickname(),
                user.getSpiritId(),
                user.currentStreak(gameClock.today()),
                collectionOf(user, collections).collectionRate());
    }

    public PlayerCard card(User user) {
        return card(user, collectionsOf(List.of(user.getId())));
    }

    // 시대별 완료 수. 완료가 없는 시대는 뺀다.
    public List<EraCount> restoredByEra(User user, Map<Long, PlayerCollection> collections) {
        PlayerCollection collection = collectionOf(user, collections);
        return Arrays.stream(Era.values())
                .filter(era -> collection.completedCount(era) > 0)
                .map(era -> new EraCount(era, collection.completedCount(era)))
                .toList();
    }

    private PlayerCollection collectionOf(User user, Map<Long, PlayerCollection> collections) {
        return collections.getOrDefault(user.getId(), PlayerCollection.NONE);
    }
}
