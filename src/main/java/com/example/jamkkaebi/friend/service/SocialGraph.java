package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.domain.FriendRelation;
import com.example.jamkkaebi.friend.repository.BlockRepository;
import com.example.jamkkaebi.friend.repository.FriendRequestRepository;
import com.example.jamkkaebi.friend.repository.FriendshipRepository;
import org.springframework.stereotype.Component;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 두 사용자 사이의 관계를 판단한다
 *
 */
@Component
public class SocialGraph {

    private final FriendshipRepository friendshipRepository;
    private final FriendRequestRepository friendRequestRepository;
    private final BlockRepository blockRepository;

    public SocialGraph(FriendshipRepository friendshipRepository,
                       FriendRequestRepository friendRequestRepository,
                       BlockRepository blockRepository) {
        this.friendshipRepository = friendshipRepository;
        this.friendRequestRepository = friendRequestRepository;
        this.blockRepository = blockRepository;
    }

    // 차단 관계인가
    public boolean blockedEitherWay(Long userId, Long otherUserId) {
        return blockRepository.existsBetween(userId, otherUserId);
    }

    public boolean areFriends(Long userId, Long otherUserId) {
        return friendshipRepository.existsBetween(userId, otherUserId);
    }

    public List<Long> friendIdsOf(Long userId) {
        return friendshipRepository.findAllOf(userId).stream()
                .map(friendship -> friendship.otherThan(userId))
                .toList();
    }

    public FriendRelation relation(Long userId, Long otherUserId, LocalDateTime now) {
        if (userId.equals(otherUserId)) {
            return FriendRelation.SELF;
        }
        if (areFriends(userId, otherUserId)) {
            return FriendRelation.FRIEND;
        }
        boolean sent = friendRequestRepository.findByFromUserIdAndToUserId(userId, otherUserId)
                .filter(request -> request.isOutstanding(now))
                .isPresent();
        if (sent) {
            return FriendRelation.REQUEST_SENT;
        }
        boolean received = friendRequestRepository.findByFromUserIdAndToUserId(otherUserId, userId)
                .filter(request -> request.isAwaitingResponse(now))
                .isPresent();
        return received ? FriendRelation.REQUEST_RECEIVED : FriendRelation.NONE;
    }

    public long friendCountOf(Long userId) {
        return friendshipRepository.countOf(userId);
    }
}
